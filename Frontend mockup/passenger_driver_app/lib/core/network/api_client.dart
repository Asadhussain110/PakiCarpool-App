import 'dart:convert';

import 'package:cookie_jar/cookie_jar.dart';
import 'package:dio/dio.dart';
import 'package:dio_cookie_manager/dio_cookie_manager.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

const localBuild = bool.fromEnvironment('LOCAL_BUILD');
const defaultApiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'https://api.pakicarpool.pk/api/v1',
);
const apiBaseUrlStorageKey = 'pakicarpool.apiBaseUrl';
const localHCaptchaSiteKey = '10000000-ffff-ffff-ffff-000000000001';

String apiBaseUrl = defaultApiBaseUrl;

String normalizeApiBaseUrl(String value) {
  final uri = Uri.tryParse(value.trim());
  if (uri == null ||
      !uri.hasAuthority ||
      !const {'http', 'https'}.contains(uri.scheme) ||
      !uri.path.replaceAll(RegExp(r'/+$'), '').endsWith('/api/v1')) {
    throw const FormatException(
      'Use an HTTP or HTTPS server address ending in /api/v1.',
    );
  }
  return uri.replace(path: uri.path.replaceAll(RegExp(r'/+$'), '')).toString();
}

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
  CancelToken _sessionCancelToken = CancelToken();
  bool _sessionBlocked = false;
  void Function()? onLivenessRequired;
  void Function()? onSessionExpired;

  static String _normalizeBaseUrl(String value) {
    final normalized = normalizeApiBaseUrl(value);
    return normalized.endsWith('/') ? normalized : '$normalized/';
  }

  String get baseUrl => dio.options.baseUrl;

  void setBaseUrl(String value) {
    final normalized = _normalizeBaseUrl(value);
    dio.options.baseUrl = normalized;
    _refreshDio.options.baseUrl = normalized;
    apiBaseUrl = normalized;
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

  Future<void> saveAccessToken(String token) async {
    _sessionBlocked = false;
    if (_sessionCancelToken.isCancelled) _sessionCancelToken = CancelToken();
    await _secureStorage.write(key: accessTokenStorageKey, value: token);
  }

  Future<void> saveSessionMetadata(String sessionJson) =>
      _secureStorage.write(key: sessionMetadataStorageKey, value: sessionJson);

  Future<String?> readSessionMetadata() =>
      _secureStorage.read(key: sessionMetadataStorageKey);

  Future<String?> getValidAccessToken() async {
    final token = await _secureStorage.read(key: accessTokenStorageKey);
    if (!_sessionBlocked && _isExpiring(token) && !_isLivenessPending(token)) {
      final refreshedToken = await _refreshAccessToken();
      return _sessionBlocked ? null : refreshedToken ?? token;
    }
    return _sessionBlocked ? null : token;
  }

  Future<void> refreshIfExpiring() async {
    final token = await _secureStorage.read(key: accessTokenStorageKey);
    if (!_sessionBlocked && _isExpiring(token) && !_isLivenessPending(token)) {
      await _refreshAccessToken();
    }
  }

  Future<void> clearSession() async {
    _sessionBlocked = true;
    if (!_sessionCancelToken.isCancelled) {
      _sessionCancelToken.cancel('The session has ended.');
    }
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
    if (_sessionBlocked && !_isAuthenticationPath(options.path)) {
      handler.reject(_blockedRequest(options));
      return;
    }

    if (!_isAuthenticationPath(options.path)) {
      options.cancelToken ??= _sessionCancelToken;
    }

    var token = await _secureStorage.read(key: accessTokenStorageKey);
    if (!_isAuthenticationPath(options.path) &&
        _isExpiring(token) &&
        !_isLivenessPending(token)) {
      final refreshedToken = await _refreshAccessToken();
      if (_sessionBlocked) {
        handler.reject(_blockedRequest(options));
        return;
      }
      token = refreshedToken ?? token;
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
      return expiry.difference(DateTime.now().toUtc()) <
          const Duration(minutes: 5);
    } catch (_) {
      return false;
    }
  }

  bool _isLivenessPending(String? token) {
    final payload = _tokenPayload(token);
    return payload?['livenessPending'] == true ||
        payload?['livenessPending'] == 'true';
  }

  Map<String, dynamic>? _tokenPayload(String? token) {
    if (token == null) return null;
    final segments = token.split('.');
    if (segments.length != 3) return null;
    try {
      final payload = jsonDecode(
        utf8.decode(base64Url.decode(base64Url.normalize(segments[1]))),
      );
      if (payload is Map<String, dynamic>) return payload;
      if (payload is Map) {
        return payload.map((key, value) => MapEntry(key.toString(), value));
      }
    } catch (_) {}
    return null;
  }

  bool _isAuthenticationPath(String path) => path.contains('auth/');

  DioException _blockedRequest(RequestOptions options) => DioException(
    requestOptions: options,
    type: DioExceptionType.cancel,
    message: 'The session ended before this request could be sent.',
  );

  Future<void> _refreshOnUnauthorized(
    DioException error,
    ErrorInterceptorHandler handler,
  ) async {
    final options = error.requestOptions;
    if (error.response?.statusCode != 401 ||
        _isAuthenticationPath(options.path) ||
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
    } on DioException catch (error) {
      if (error.response?.statusCode == 401) {
        final body = _asJsonMap(error.response?.data);
        final code = body['error']?.toString();
        _sessionBlocked = true;
        if (code == 'LIVENESS_REQUIRED' && await _isDriverSession()) {
          if (!_sessionCancelToken.isCancelled) {
            _sessionCancelToken.cancel('Driver liveness is required.');
          }
          await _secureStorage.delete(key: accessTokenStorageKey);
          final metadata = await readSessionMetadata();
          if (metadata != null) {
            final session = _asJsonMap(jsonDecode(metadata));
            session['accessToken'] = '';
            session['livenessPending'] = true;
            await saveSessionMetadata(jsonEncode(session));
          }
          onLivenessRequired?.call();
        } else {
          await clearSession();
          onSessionExpired?.call();
        }
      }
      return null;
    }
  }

  Future<bool> _isDriverSession() async {
    try {
      final metadata = await readSessionMetadata();
      if (metadata == null) return false;
      final session = _asJsonMap(jsonDecode(metadata));
      return session['role']?.toString().toLowerCase() == 'driver';
    } catch (_) {
      return false;
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
  const ApiFailure({
    required this.message,
    this.code,
    this.statusCode,
    this.fieldErrors = const {},
  });

  final String message;
  final String? code;
  final int? statusCode;
  final Map<String, List<String>> fieldErrors;

  factory ApiFailure.fromDio(DioException error) {
    final body = _asJsonMap(error.response?.data);
    final code = body['error']?.toString();
    final responseMessage = [body['message'], body['detail'], body['title']]
        .map((value) => value?.toString() ?? '')
        .firstWhere((value) => value.trim().isNotEmpty, orElse: () => '');
    final fieldErrors = <String, List<String>>{};
    final rawErrors = body['errors'];
    if (rawErrors is Map) {
      for (final entry in rawErrors.entries) {
        final value = entry.value;
        final messages = value is Iterable
            ? value.map((item) => item.toString()).toList()
            : value == null
            ? <String>[]
            : <String>[value.toString()];
        if (messages.isNotEmpty) {
          fieldErrors[entry.key.toString()] = messages;
        }
      }
    }
    final validationMessage = [
      if (responseMessage.isNotEmpty) responseMessage,
      for (final entry in fieldErrors.entries)
        '${entry.key}: ${entry.value.join(' ')}',
    ].join('\n');
    final message = validationMessage.isNotEmpty
        ? validationMessage
        : switch (error.type) {
            DioExceptionType.connectionTimeout ||
            DioExceptionType.sendTimeout ||
            DioExceptionType.receiveTimeout =>
              'The request timed out. Try again.',
            DioExceptionType.connectionError => 'Could not connect. Check your internet connection and try again.',
            _ =>
              responseMessage.isNotEmpty
                  ? responseMessage
                  : 'Something went wrong. Try again.',
          };
    return ApiFailure(
      message: message,
      code: code,
      statusCode: error.response?.statusCode,
      fieldErrors: fieldErrors,
    );
  }

  @override
  String toString() => message;
}
