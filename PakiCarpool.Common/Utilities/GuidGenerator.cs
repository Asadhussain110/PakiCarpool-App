namespace PakiCarpool.Common.Utilities;

/// <summary>
/// Sequential GUID generator for database-friendly primary keys.
/// Sequential GUIDs reduce index fragmentation in PostgreSQL.
/// </summary>
public static class GuidGenerator
{
    /// <summary>
    /// Generates a new sequential GUID with a timestamp prefix for optimized DB indexing.
    /// </summary>
    public static Guid NewSequentialGuid()
    {
        var timestamp = DateTime.UtcNow.Ticks;
        var guidBytes = Guid.NewGuid().ToByteArray();
        var timestampBytes = BitConverter.GetBytes(timestamp);

        // Replace first 6 bytes with timestamp for sequential ordering
        if (BitConverter.IsLittleEndian)
            Array.Reverse(timestampBytes);

        Buffer.BlockCopy(timestampBytes, 2, guidBytes, 0, 6);
        return new Guid(guidBytes);
    }
}
