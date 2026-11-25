import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/api_client.dart';
import '../models/auth_session.dart';

final authRepositoryProvider = Provider<AuthRepository>(
  (ref) => ApiAuthRepository(ref.watch(apiClientProvider)),
);

abstract interface class AuthRepository {
  Future<AuthSession> signIn({
    required String email,
    required String password,
    String? hCaptchaToken,
  });

  Future<void> register({
    required String fullName,
    required String email,
    required String password,
    required PakiRole role,
  });

  Future<void> verifyEmail({required String email, required String otp});

  Future<void> resendEmailVerification({required String email});

  Future<void> forgotPassword({required String email});

  Future<void> resetPassword({
    required String email,
    required String code,
    required String newPassword,
  });

  Future<String> googleAuthorizationUrl({required PakiRole role});

  Future<AuthSession> completeGoogleAuthorization({
    required String code,
    required String state,
  });
}

class ApiAuthRepository implements AuthRepository {
  const ApiAuthRepository(this._apiClient);

  final ApiClient _apiClient;

  @override
  Future<AuthSession> signIn({
    required String email,
    required String password,
    String? hCaptchaToken,
  }) async {
    final request = <String, Object?>{'email': email, 'password': password};
    if (hCaptchaToken != null && hCaptchaToken.isNotEmpty) {
      request['hCaptchaToken'] = hCaptchaToken;
    }
    final response = await _send(
      () => _apiClient.post('auth/login', data: request),
    );
    return _saveSession(response);
  }

  @override
  Future<void> register({
    required String fullName,
    required String email,
    required String password,
    required PakiRole role,
  }) async {
    await _send(
      () => _apiClient.post(
        'auth/register',
        data: {
          'fullName': fullName,
          'email': email,
          'password': password,
          'role': role.apiValue,
        },
      ),
    );
  }

  @override
  Future<void> verifyEmail({required String email, required String otp}) async {
    await _send(
      () => _apiClient.post(
        'auth/verify-email',
        data: {'email': email, 'otp': otp},
      ),
    );
  }

  @override
  Future<void> resendEmailVerification({required String email}) async {
    await _send(
      () => _apiClient.post(
        'auth/verify-email/resend',
        data: {'email': email},
      ),
    );
  }

  @override
  Future<void> forgotPassword({required String email}) async {
    await _send(
      () => _apiClient.post('auth/forgot-password', data: {'email': email}),
    );
  }

  @override
  Future<void> resetPassword({
    required String email,
    required String code,
    required String newPassword,
  }) async {
    await _send(
      () => _apiClient.post(
        'auth/reset-password',
        data: {'email': email, 'code': code, 'newPassword': newPassword},
      ),
    );
  }

  @override
  Future<String> googleAuthorizationUrl({required PakiRole role}) async {
    try {
      final response = await _apiClient.get(
        'auth/google',
        queryParameters: {'role': role.apiValue},
      );
      final envelope = _map(response.data);
      final data = _map(envelope['data']);
      final url = data['authUrl']?.toString();
      if (url == null || url.isEmpty) {
        throw const ApiFailure(message: 'Google sign-in could not be started.');
      }
      return url;
    } on DioException catch (error) {
      throw ApiFailure.fromDio(error);
    }
  }

  @override
  Future<AuthSession> completeGoogleAuthorization({
    required String code,
    required String state,
  }) async {
    final response = await _send(
      () => _apiClient.get(
        'auth/google/callback',
        queryParameters: {'code': code, 'state': state},
      ),
    );
    return _saveSession(response);
  }

  Future<Map<String, dynamic>> _send(
    Future<Response<dynamic>> Function() request,
  ) async {
    try {
      final response = await request();
      return _map(response.data);
    } on DioException catch (error) {
      throw ApiFailure.fromDio(error);
    }
  }

  Future<AuthSession> _saveSession(Map<String, dynamic> envelope) async {
    final data = _map(envelope['data']);
    final session = AuthSession.fromJson(data);
    if (session.accessToken.isEmpty) {
      throw const ApiFailure(
        message: 'The server did not return an access token.',
      );
    }
    await _apiClient.saveAccessToken(session.accessToken);
    await _apiClient.saveSessionMetadata(jsonEncode(session.toJson()));
    return session;
  }
}

Map<String, dynamic> _map(Object? value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) {
    return value.map((key, item) => MapEntry(key.toString(), item));
  }
  return const {};
}
