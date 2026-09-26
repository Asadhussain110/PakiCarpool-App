import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

const adminApiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'https://api.pakicarpool.pk/api/v1',
);

final adminSessionProvider =
    NotifierProvider<AdminSessionController, AdminSession?>(
      AdminSessionController.new,
    );

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
    onUnauthorized: () => ref.read(adminSessionProvider.notifier).clear(),
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
          final token = _accessToken();
          if (token != null && token.isNotEmpty) {
            options.headers['Authorization'] = 'Bearer $token';
          }
          handler.next(options);
        },
      ),
    );
  }

  final String? Function() _accessToken;
  final void Function()? onUnauthorized;
  final Dio _dio;

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
      final data = envelope['data'];
      return data is List ? data.map(_map).toList() : const [];
    } on DioException catch (error) {
      if (error.response?.statusCode == 401) onUnauthorized?.call();
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
      if (error.response?.statusCode == 401) onUnauthorized?.call();
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
      if (error.response?.statusCode == 401) onUnauthorized?.call();
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
