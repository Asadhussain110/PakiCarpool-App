enum PakiRole { passenger, driver, admin }

extension PakiRoleApiValue on PakiRole {
  String get apiValue => switch (this) {
    PakiRole.passenger => 'Passenger',
    PakiRole.driver => 'Driver',
    PakiRole.admin => 'Admin',
  };
}

class AuthSession {
  const AuthSession({
    required this.accessToken,
    required this.userId,
    required this.email,
    required this.fullName,
    required this.role,
    required this.livenessPending,
  });

  final String accessToken;
  final String userId;
  final String email;
  final String fullName;
  final PakiRole role;
  final bool livenessPending;

  Map<String, Object?> toJson() => {
    'accessToken': accessToken,
    'userId': userId,
    'email': email,
    'fullName': fullName,
    'role': role.apiValue,
    'livenessPending': livenessPending,
  };

  factory AuthSession.fromJson(Map<String, dynamic> json) {
    final rawRole = json['role'];
    final role = rawRole is int
        ? switch (rawRole) {
            1 => PakiRole.driver,
            2 => PakiRole.admin,
            _ => PakiRole.passenger,
          }
        : _roleFromString(rawRole?.toString());

    return AuthSession(
      accessToken: json['accessToken']?.toString() ?? '',
      userId: json['userId']?.toString() ?? '',
      email: json['email']?.toString() ?? '',
      fullName: json['fullName']?.toString() ?? '',
      role: role,
      livenessPending: json['livenessPending'] == true,
    );
  }
}

PakiRole _roleFromString(String? value) {
  switch (value?.toLowerCase()) {
    case 'driver':
      return PakiRole.driver;
    case 'admin':
      return PakiRole.admin;
    default:
      return PakiRole.passenger;
  }
}
