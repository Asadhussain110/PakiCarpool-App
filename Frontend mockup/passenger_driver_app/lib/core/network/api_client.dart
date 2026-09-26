import 'dart:convert';

import 'package:cookie_jar/cookie_jar.dart';
import 'package:dio/dio.dart';
import 'package:dio_cookie_manager/dio_cookie_manager.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

const apiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'https://api.pakicarpool.pk/api/v1',
);

const accessTokenStorageKey = 'pakicarpool.accessToken';
const sessionMetadataStorageKey = 'pakicarpool.session';

final secureStorageProvider = Provider<FlutterSecureStorage>(
  (ref) => const FlutterSecureStorage(),
);

final apiClientProvider = Provider<ApiClient>((ref) {
  final client = ApiClient(
    baseUrl: apiBaseUrl,
    secureStorage: ref.watch(secureStorageProvider),
  );
  ref.onDispose(client.close);
  return client;
});

class ApiClient {
  ApiClient({
    required String baseUrl,
    required FlutterSecureStorage secureStorage,
  }) : _secureStorage = secureStorage,
       _cookieJar = PersistCookieJar(
         storage: _SecureCookieStorage(secureStorage),
       ),
       dio = Dio(
         BaseOptions(
           baseUrl: _normalizeBaseUrl(baseUrl),
           connectTimeout: const Duration(seconds: 15),
           receiveTimeout: const Duration(seconds: 20),
           sendTimeout: const Duration(seconds: 20),
           headers: const {'Accept': 'application/json'},
         ),
       ),
       _refreshDio = Dio(
         BaseOptions(
           baseUrl: _normalizeBaseUrl(baseUrl),
           connectTimeout: const Duration(seconds: 15),
           receiveTimeout: const Duration(seconds: 20),
           sendTimeout: const Duration(seconds: 20),
           headers: const {'Accept': 'application/json'},
         ),
       ) {
    dio.interceptors.add(CookieManager(_cookieJar));
    _refreshDio.interceptors.add(CookieManager(_cookieJar));
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: _attachAccessToken,
        onError: _refreshOnUnauthorized,
      ),
    );
  }

  final FlutterSecureStorage _secureStorage;
  final PersistCookieJar _cookieJar;
  final Dio dio;
  final Dio _refreshDio;
  Future<String?>? _refreshInFlight;

  static String _normalizeBaseUrl(String value) {
    final trimmed = value.trim();
    return trimmed.endsWith('/') ? trimmed : '$trimmed/';
  }

  Future<Response<dynamic>> get(
    String path, {
    Map<String, dynamic>? queryParameters,
  }) => dio.get<dynamic>(path, queryParameters: queryParameters);

  Future<Response<dynamic>> post(
    String path, {
    Object? data,
    Map<String, dynamic>? queryParameters,
  }) => dio.post<dynamic>(path, data: data, queryParameters: queryParameters);

  Future<Response<dynamic>> put(String path, {Object? data}) =>
      dio.put<dynamic>(path, data: data);

  Future<Response<dynamic>> delete(String path, {Object? data}) =>
      dio.delete<dynamic>(path, data: data);

  Future<void> saveAccessToken(String token) =>
      _secureStorage.write(key: accessTokenStorageKey, value: token);

  Future<void> saveSessionMetadata(String sessionJson) =>
      _secureStorage.write(key: sessionMetadataStorageKey, value: sessionJson);

  Future<String?> readSessionMetadata() =>
      _secureStorage.read(key: sessionMetadataStorageKey);

  Future<String?> getValidAccessToken() async {
    final token = await _secureStorage.read(key: accessTokenStorageKey);
    if (_isExpiring(token)) {
      return await _refreshAccessToken() ?? token;
    }
    return token;
  }

  Future<void> clearSession() async {
    await _secureStorage.delete(key: accessTokenStorageKey);
    await _secureStorage.delete(key: sessionMetadataStorageKey);
    await _cookieJar.deleteAll();
  }

  Future<void> close() async {
    dio.close(force: true);
    _refreshDio.close(force: true);
  }

  Future<void> _attachAccessToken(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    var token = await _secureStorage.read(key: accessTokenStorageKey);
    if (!options.path.contains('auth/') && _isExpiring(token)) {
      token = await _refreshAccessToken() ?? token;
    }
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }

  bool _isExpiring(String? token) {
    if (token == null) return false;
    final segments = token.split('.');
    if (segments.length != 3) return false;
    try {
      final payload = jsonDecode(
        utf8.decode(base64Url.decode(base64Url.normalize(segments[1]))),
      );
      if (payload is! Map || payload['exp'] is! num) return false;
      final expiry = DateTime.fromMillisecondsSinceEpoch(
        (payload['exp'] as num).toInt() * 1000,
        isUtc: true,
      );
      return expiry.difference(DateTime.now().toUtc()) <=
          const Duration(minutes: 1);
    } catch (_) {
      return false;
    }
  }

  Future<void> _refreshOnUnauthorized(
    DioException error,
    ErrorInterceptorHandler handler,
  ) async {
    final options = error.requestOptions;
    if (error.response?.statusCode != 401 ||
        options.path.contains('auth/') ||
        options.extra['authRetried'] == true) {
      handler.next(error);
      return;
    }

    final token = await _refreshAccessToken();
    if (token == null) {
      handler.next(error);
      return;
    }

    options.headers['Authorization'] = 'Bearer $token';
    options.extra['authRetried'] = true;
    try {
      handler.resolve(await dio.fetch<dynamic>(options));
    } on DioException {
      handler.next(error);
    }
  }

  Future<String?> _refreshAccessToken() async {
    final future = _refreshInFlight ??= _performRefresh();
    try {
      return await future;
    } finally {
      if (identical(_refreshInFlight, future)) {
        _refreshInFlight = null;
      }
    }
  }

  Future<String?> _performRefresh() async {
    try {
      await _cookieJar.forceInit();
      final response = await _refreshDio.post<dynamic>('auth/refresh');
      final envelope = _asJsonMap(response.data);
      final data = _asJsonMap(envelope['data']);
      final token = data['accessToken']?.toString();
      if (token == null || token.isEmpty) return null;
      await saveAccessToken(token);
      final metadata = await readSessionMetadata();
      if (metadata != null) {
        final session = _asJsonMap(jsonDecode(metadata));
        session['accessToken'] = token;
        await saveSessionMetadata(jsonEncode(session));
      }
      return token;
    } on DioException {
      await _secureStorage.delete(key: accessTokenStorageKey);
      await _secureStorage.delete(key: sessionMetadataStorageKey);
      return null;
    }
  }
}

class _SecureCookieStorage implements Storage {
  const _SecureCookieStorage(this.secureStorage);

  final FlutterSecureStorage secureStorage;

  String _key(String key) => 'pakicarpool.cookie.$key';

  @override
  Future<void> init(bool persistSession, bool ignoreExpires) async {}

  @override
  Future<String?> read(String key) => secureStorage.read(key: _key(key));

  @override
  Future<void> write(String key, String value) =>
      secureStorage.write(key: _key(key), value: value);

  @override
  Future<void> delete(String key) => secureStorage.delete(key: _key(key));

  @override
  Future<void> deleteAll(List<String> keys) async {
    for (final key in keys) {
      await delete(key);
    }
  }
}

Map<String, dynamic> _asJsonMap(Object? value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) {
    return value.map((key, item) => MapEntry(key.toString(), item));
  }
  return const {};
}

class ApiFailure implements Exception {
  const ApiFailure({required this.message, this.code, this.statusCode});

  final String message;
  final String? code;
  final int? statusCode;

  factory ApiFailure.fromDio(DioException error) {
    final body = _asJsonMap(error.response?.data);
    final code = body['error']?.toString();
    final serverMessage = body['message']?.toString();
    final message = switch (error.type) {
      DioExceptionType.connectionTimeout ||
      DioExceptionType.sendTimeout ||
      DioExceptionType.receiveTimeout => 'The request timed out. Try again.',
      DioExceptionType.connectionError =>
        'Could not connect. Check your internet connection and try again.',
      _ =>
        serverMessage?.isNotEmpty == true
            ? serverMessage!
            : 'Something went wrong. Try again.',
    };
    return ApiFailure(
      message: message,
      code: code,
      statusCode: error.response?.statusCode,
    );
  }

  @override
  String toString() => message;
}
