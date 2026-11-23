import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/auth/models/app_session.dart';
import 'api_client.dart';

final mobileApiProvider = Provider<MobileApi>(
  (ref) => MobileApi(
    ref.watch(apiClientProvider),
    onUnauthorized: () => ref.read(appSessionProvider.notifier).clear(),
  ),
);

class MobileApi {
  const MobileApi(this.client, {this.onUnauthorized});

  final ApiClient client;
  final void Function()? onUnauthorized;

  Future<Map<String, dynamic>> getData(
    String path, {
    Map<String, dynamic>? query,
  }) => _send(path, () => client.get(path, queryParameters: query));

  Future<Map<String, dynamic>> getBareData(
    String path, {
    Map<String, dynamic>? query,
  }) => _send(path, () => client.get(path, queryParameters: query), bare: true);

  Future<List<Map<String, dynamic>>> getListData(
    String path, {
    Map<String, dynamic>? query,
  }) async {
    try {
      final response = await client.get(path, queryParameters: query);
      final envelope = _map(response.data);
      final data = envelope['data'];
      return data is List ? data.map(_map).toList() : const [];
    } on DioException catch (error) {
      if (error.response?.statusCode == 401) onUnauthorized?.call();
      throw ApiFailure.fromDio(error);
    }
  }

  Future<Map<String, dynamic>> postData(String path, {Object? body}) =>
      _send(path, () => client.post(path, data: body));

  Future<Map<String, dynamic>> postBareData(String path, {Object? body}) =>
      _send(path, () => client.post(path, data: body), bare: true);

  Future<Map<String, dynamic>> putData(String path, {Object? body}) =>
      _send(path, () => client.put(path, data: body));

  Future<Map<String, dynamic>> deleteData(String path, {Object? body}) =>
      _send(path, () => client.delete(path, data: body));

  Future<List<Map<String, dynamic>>> getItems(
    String path, {
    Map<String, dynamic>? query,
  }) async {
    final data = await getData(path, query: query);
    final items = data['items'];
    if (items is! List) return const [];
    return items.map(_map).toList(growable: false);
  }

  Future<Response<List<int>>> getBytes(
    String path, {
    Map<String, dynamic>? query,
  }) => client.dio.get<List<int>>(
    path,
    queryParameters: query,
    options: Options(responseType: ResponseType.bytes),
  );

  Future<Map<String, dynamic>> _send(
    String path,
    Future<Response<dynamic>> Function() request, {
    bool bare = false,
  }) async {
    try {
      final response = await request();
      if (bare || _isBareLivenessPath(path)) {
        return _map(response.data);
      }
      final envelope = _map(response.data);
      return _map(envelope['data']);
    } on DioException catch (error) {
      if (error.response?.statusCode == 401) onUnauthorized?.call();
      throw ApiFailure.fromDio(error);
    }
  }
}

bool _isBareLivenessPath(String path) {
  final cleanPath = path.replaceFirst(RegExp(r'^/'), '');
  return switch (cleanPath) {
    'driver/liveness/challenge' || 'driver/liveness' => true,
    _ => false,
  };
}

Map<String, dynamic> _map(Object? value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) {
    return value.map((key, item) => MapEntry(key.toString(), item));
  }
  return const {};
}
