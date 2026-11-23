import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

const localBuild = bool.fromEnvironment('LOCAL_BUILD');
const defaultAdminApiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'https://api.pakicarpool.pk/api/v1',
);
const adminApiBaseUrlStorageKey = 'pakicarpool.apiBaseUrl';

String adminApiBaseUrl = defaultAdminApiBaseUrl;

String normalizeAdminApiBaseUrl(String value) {
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

final adminSessionProvider =
    NotifierProvider<AdminSessionController, AdminSession?>(
      AdminSessionController.new,
    );

final adminExpiryNoticeProvider =
    NotifierProvider<AdminExpiryNoticeController, bool?>(
      AdminExpiryNoticeController.new,
    );

class AdminExpiryNoticeController extends Notifier<bool?> {
  @override
  bool? build() => null;

  void set(bool actionFailed) => state ??= actionFailed;

  void clear() => state = null;
}

class AdminSession {
  const AdminSession({
    required this.accessToken,
    required this.userId,
    required this.email,
    required this.fullName,
  });

  final String accessToken;
  final String userId;
  final String email;
  final String fullName;

  DateTime? get expiresAt {
    final segments = accessToken.split('.');
    if (segments.length != 3) return null;
    try {
      final payload = jsonDecode(
        utf8.decode(base64Url.decode(base64Url.normalize(segments[1]))),
      );
      if (payload is! Map || payload['exp'] is! num) return null;
      return DateTime.fromMillisecondsSinceEpoch(
        (payload['exp'] as num).toInt() * 1000,
        isUtc: true,
      );
    } catch (_) {
      return null;
    }
  }
}

class AdminSessionController extends Notifier<AdminSession?> {
  @override
  AdminSession? build() => null;

  void set(AdminSession session) => state = session;

  void clear() => state = null;
}

final adminApiProvider = Provider<AdminApi>(
  (ref) => AdminApi(
    baseUrl: adminApiBaseUrl,
    accessToken: () => ref.read(adminSessionProvider)?.accessToken,
    onUnauthorized: ({required actionFailed}) {
      ref.read(adminExpiryNoticeProvider.notifier).set(actionFailed);
      ref.read(adminSessionProvider.notifier).clear();
    },
  ),
);

class AdminApi {
  AdminApi({
    required String baseUrl,
    required this._accessToken,
    this.onUnauthorized,
  }) : _dio = Dio(
         BaseOptions(
           baseUrl: baseUrl.endsWith('/') ? baseUrl : '$baseUrl/',
           connectTimeout: const Duration(seconds: 15),
           receiveTimeout: const Duration(seconds: 25),
           sendTimeout: const Duration(seconds: 25),
           headers: const {'Accept': 'application/json'},
         ),
       ) {
    _dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          if (_isAuthEndpoint(options.path)) {
            handler.next(options);
            return;
          }

          final token = _accessToken();
          if (_sessionBlocked || token == null || _isExpired(token)) {
            expireSession(actionFailed: _isAction(options));
            handler.reject(
              DioException(
                requestOptions: options,
                type: DioExceptionType.cancel,
                message: 'Your session has expired.',
              ),
            );
            return;
          }

          options.headers['Authorization'] = 'Bearer $token';
          options.cancelToken ??= _sessionCancelToken;
          if (_isAction(options)) {
            _inFlightActions++;
            options.extra['adminActionTracked'] = true;
          }
          handler.next(options);
        },
        onResponse: (response, handler) {
          _finishAction(response.requestOptions);
          handler.next(response);
        },
        onError: (error, handler) {
          _finishAction(error.requestOptions);
          if (error.response?.statusCode == 401 &&
              !_isAuthEndpoint(error.requestOptions.path) &&
              _accessToken() != null) {
            expireSession(actionFailed: _isAction(error.requestOptions));
          }
          handler.next(error);
        },
      ),
    );
  }

  final String? Function() _accessToken;
  final void Function({required bool actionFailed})? onUnauthorized;
  final Dio _dio;
  CancelToken _sessionCancelToken = CancelToken();
  bool _sessionBlocked = false;
  int _inFlightActions = 0;

  bool get hasInFlightActions => _inFlightActions > 0;

  String get baseUrl => _dio.options.baseUrl;

  void setBaseUrl(String value) {
    final normalized = normalizeAdminApiBaseUrl(value);
    _dio.options.baseUrl = normalized.endsWith('/')
        ? normalized
        : '$normalized/';
    adminApiBaseUrl = _dio.options.baseUrl;
  }

  void resumeSession() {
    _sessionBlocked = false;
    if (_sessionCancelToken.isCancelled) _sessionCancelToken = CancelToken();
  }

  void expireSession({required bool actionFailed}) {
    if (_sessionBlocked) return;
    _sessionBlocked = true;
    if (!_sessionCancelToken.isCancelled) {
      _sessionCancelToken.cancel('The session has expired.');
    }
    onUnauthorized?.call(actionFailed: actionFailed);
  }

  bool _isAuthEndpoint(String path) {
    final normalized = path.replaceFirst(RegExp(r'^/'), '');
    return normalized.startsWith('auth/') && normalized != 'auth/logout';
  }

  bool _isAction(RequestOptions options) => options.method != 'GET';

  bool _isExpired(String token) {
    final segments = token.split('.');
    if (segments.length != 3) return true;
    try {
      final payload = jsonDecode(
        utf8.decode(base64Url.decode(base64Url.normalize(segments[1]))),
      );
      if (payload is! Map || payload['exp'] is! num) return true;
      final expiresAt = DateTime.fromMillisecondsSinceEpoch(
        (payload['exp'] as num).toInt() * 1000,
        isUtc: true,
      );
      return !DateTime.now().toUtc().isBefore(expiresAt);
    } catch (_) {
      return true;
    }
  }

  void _finishAction(RequestOptions options) {
    if (options.extra.remove('adminActionTracked') == true) {
      _inFlightActions--;
    }
  }

  Future<String> login({
    required String email,
    required String password,
  }) async {
    final data = await _request(
      () => _dio.post<dynamic>(
        'auth/admin/login',
        data: {'email': email, 'password': password},
      ),
    );
    final token = data['partialToken']?.toString();
    if (token == null || token.isEmpty) {
      throw const AdminApiError('The server did not return a step-one token.');
    }
    return token;
  }

  Future<AdminSession> verifyTotp({
    required String code,
    required String partialToken,
  }) async {
    final data = await _request(
      () => _dio.post<dynamic>(
        'auth/admin/totp-verify',
        data: {'code': code, 'partialToken': partialToken},
      ),
    );
    final token = data['accessToken']?.toString();
    if (token == null || token.isEmpty) {
      throw const AdminApiError(
        'The server did not return an admin access token.',
      );
    }
    return AdminSession(
      accessToken: token,
      userId: data['userId']?.toString() ?? '',
      email: data['email']?.toString() ?? '',
      fullName: data['fullName']?.toString() ?? 'Administrator',
    );
  }

  Future<Map<String, dynamic>> get(
    String path, {
    Map<String, dynamic>? query,
  }) => _request(() => _dio.get<dynamic>(path, queryParameters: query));

  Future<List<Map<String, dynamic>>> getList(
    String path, {
    Map<String, dynamic>? query,
  }) async {
    try {
      final response = await _dio.get<dynamic>(path, queryParameters: query);
      final envelope = _map(response.data);
      final payload = envelope['data'];
      final data = payload is List ? payload : _map(payload)['items'];
      if (data is! List) {
        throw const AdminApiError(
          'The server returned an unexpected list response.',
        );
      }
      return data.map(_map).toList();
    } on DioException catch (error) {
      throw AdminApiError.fromDio(error);
    }
  }

  Future<Map<String, dynamic>> post(String path, {Object? body}) =>
      _request(() => _dio.post<dynamic>(path, data: body));

  Future<Map<String, dynamic>> put(String path, {Object? body}) =>
      _request(() => _dio.put<dynamic>(path, data: body));

  Future<Map<String, dynamic>> delete(String path) =>
      _request(() => _dio.delete<dynamic>(path));

  Future<List<Map<String, dynamic>>> getItems(
    String path, {
    Map<String, dynamic>? query,
  }) async {
    final data = await get(path, query: query);
    final items = data['items'];
    return items is List ? items.map(_map).toList() : const [];
  }

  Future<List<int>> download(String path, {Map<String, dynamic>? query}) async {
    try {
      final response = await _dio.get<List<int>>(
        path,
        queryParameters: query,
        options: Options(responseType: ResponseType.bytes),
      );
      return response.data ?? const [];
    } on DioException catch (error) {
      throw AdminApiError.fromDio(error);
    }
  }

  Future<Map<String, dynamic>> _request(
    Future<Response<dynamic>> Function() request,
  ) async {
    try {
      final response = await request();
      return _map(_map(response.data)['data']);
    } on DioException catch (error) {
      throw AdminApiError.fromDio(error);
    }
  }

  Map<String, dynamic> _map(Object? value) {
    if (value is Map<String, dynamic>) return value;
    if (value is Map) return value.map((key, item) => MapEntry('$key', item));
    return const {};
  }
}

class AdminApiError implements Exception {
  const AdminApiError(this.message, {this.code, this.statusCode});

  final String message;
  final String? code;
  final int? statusCode;

  factory AdminApiError.fromDio(DioException error) {
    final body = error.response?.data;
    final json = body is Map ? body : const <String, dynamic>{};
    final message = json['message']?.toString();
    final code = json['error']?.toString();
    return AdminApiError(
      message?.isNotEmpty == true
          ? message!
          : error.type == DioExceptionType.connectionError
          ? 'Could not connect. Check your connection and try again.'
          : 'Request failed. Try again.',
      code: code,
      statusCode: error.response?.statusCode,
    );
  }

  @override
  String toString() => message;
}
