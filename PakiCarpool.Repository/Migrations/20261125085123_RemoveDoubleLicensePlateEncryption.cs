using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PakiCarpool.Repository.EFCore.ValueConverters;

#nullable disable

namespace PakiCarpool.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDoubleLicensePlateEncryption : Migration
    {
        private delegate bool TryConvertCiphertext(
            string ciphertext,
            IReadOnlyDictionary<byte, string> base64Keys,
            byte activeVersion,
            string legacyCbcBase64Key,
            out string convertedCiphertext);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            RunFromEnvironment(MigrateAsync);
            migrationBuilder.Sql("SELECT 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back re-applies a fresh outer AES layer to every single-layer
            // value. Random nonces prevent byte-identical recovery of old ciphertext,
            // but the restored values decrypt exactly as they did before Up().
            RunFromEnvironment(RollbackAsync);
            migrationBuilder.Sql("SELECT 1;");
        }

        public static Task MigrateAsync(
            string connectionString,
            IReadOnlyDictionary<byte, string> base64Keys,
            byte activeVersion,
            string legacyCbcBase64Key,
            CancellationToken cancellationToken = default)
            => TransformAsync(
                connectionString,
                base64Keys,
                activeVersion,
                legacyCbcBase64Key,
                EncryptedStringConverter.TryRemoveDoubleEncryption,
                cancellationToken);

        public static Task RollbackAsync(
            string connectionString,
            IReadOnlyDictionary<byte, string> base64Keys,
            byte activeVersion,
            string legacyCbcBase64Key,
            CancellationToken cancellationToken = default)
            => TransformAsync(
                connectionString,
                base64Keys,
                activeVersion,
                legacyCbcBase64Key,
                EncryptedStringConverter.TryRestoreDoubleEncryption,
                cancellationToken);

        private static async Task TransformAsync(
            string connectionString,
            IReadOnlyDictionary<byte, string> base64Keys,
            byte activeVersion,
            string legacyCbcBase64Key,
            TryConvertCiphertext tryConvert,
            CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var rows = new List<(Guid Id, string Ciphertext)>();
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT \"Id\", \"LicensePlate\" FROM \"DriverCars\" FOR UPDATE;";
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    rows.Add((reader.GetFieldValue<Guid>(0), reader.GetString(1)));
            }

            foreach (var (id, ciphertext) in rows)
            {
                if (!tryConvert(
                        ciphertext,
                        base64Keys,
                        activeVersion,
                        legacyCbcBase64Key,
                        out var convertedCiphertext))
                {
                    continue;
                }

                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE \"DriverCars\" SET \"LicensePlate\" = @ciphertext WHERE \"Id\" = @id;";
                update.Parameters.AddWithValue("ciphertext", convertedCiphertext);
                update.Parameters.AddWithValue("id", id);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        private static void RunFromEnvironment(
            Func<string, IReadOnlyDictionary<byte, string>, byte, string, CancellationToken, Task> action)
        {
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("DB_CONNECTION_STRING is required for the license-plate data migration.");

            var legacyKey = Environment.GetEnvironmentVariable("AES_ENCRYPTION_KEY");
            if (string.IsNullOrWhiteSpace(legacyKey))
                legacyKey = null;
            var versionOneKey = legacyKey ?? Environment.GetEnvironmentVariable("AES_KEY_V1");
            var versionTwoKey = Environment.GetEnvironmentVariable("AES_KEY_V2");
            if (string.IsNullOrWhiteSpace(versionTwoKey))
                versionTwoKey = null;

            var keys = new Dictionary<byte, string>();
            if (!string.IsNullOrWhiteSpace(legacyKey))
                keys[0] = legacyKey;
            if (!string.IsNullOrWhiteSpace(versionOneKey))
                keys[1] = versionOneKey;
            if (!string.IsNullOrWhiteSpace(versionTwoKey))
                keys[2] = versionTwoKey;
            if (keys.Count == 0)
                throw new InvalidOperationException("The existing AES keys are required for the license-plate data migration.");

            var activeVersion = keys.Keys.Max();
            var legacyCbcKey = new[] { legacyKey, versionOneKey, versionTwoKey }
                .First(value => !string.IsNullOrWhiteSpace(value));
            action(connectionString, keys, activeVersion, legacyCbcKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
    }
}
