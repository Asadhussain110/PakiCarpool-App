import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../network/api_client.dart';

final signalRServiceProvider = Provider<SignalRService>((ref) {
  final service = SignalRService(ref.watch(apiClientProvider));
  ref.onDispose(service.dispose);
  return service;
});

class SignalRService {
  SignalRService(this._apiClient);

  final ApiClient _apiClient;
  HubConnection? _connection;
  final Map<String, List<void Function(List<Object?>?)>> _handlers = {};
  final _stateController = StreamController<HubConnectionState>.broadcast();

  Stream<HubConnectionState> get states => _stateController.stream;
  HubConnectionState? get state => _connection?.state;

  Future<void> connect() async {
    if (_connection?.state == HubConnectionState.Connected ||
        _connection?.state == HubConnectionState.Connecting) {
      return;
    }

    final options = HttpConnectionOptions(
      accessTokenFactory: () async =>
          await _apiClient.getValidAccessToken() ?? '',
    );
    final hubUrl = apiBaseUrl
        .replaceFirst('/api/v1', '/hubs/pakicarpool')
        .replaceAll(RegExp(r'/+$'), '');
    final connection = HubConnectionBuilder()
        .withUrl(hubUrl, options: options)
        .withAutomaticReconnect(retryDelays: [2000, 5000, 10000, 20000, 30000])
        .build();
    connection.onclose(({Exception? error}) => _emitState());
    connection.onreconnecting(({Exception? error}) => _emitState());
    connection.onreconnected(({String? connectionId}) {
      _emitState();
      for (final entry in _handlers.entries) {
        for (final handler in entry.value) {
          connection.on(entry.key, handler);
        }
      }
    });
    _connection = connection;
    await connection.start();
    _emitState();
    for (final entry in _handlers.entries) {
      for (final handler in entry.value) {
        connection.on(entry.key, handler);
      }
    }
  }

  void on(String event, void Function(List<Object?>?) handler) {
    _handlers.putIfAbsent(event, () => []).add(handler);
    _connection?.on(event, handler);
  }

  Future<void> joinRide(String rideId) async {
    await _ensureConnected();
    await _connection!.invoke('JoinRideGroup', args: [rideId]);
  }

  Future<void> joinPassengerRide(String rideId, String passengerId) async {
    await _ensureConnected();
    await _connection!.invoke(
      'JoinPassengerRideGroup',
      args: [rideId, passengerId],
    );
  }

  Future<void> leaveRide(String rideId) async {
    final connection = _connection;
    if (connection?.state == HubConnectionState.Connected) {
      await connection!.invoke('LeaveRideGroup', args: [rideId]);
    }
  }

  Future<void> sendLocation(String rideId, double lat, double lng) async {
    await _ensureConnected();
    await _connection!.invoke('UpdateLocation', args: [rideId, lat, lng]);
  }

  Future<void> sendChat({
    required String bookingId,
    required String receiverId,
    required String message,
  }) async {
    await _ensureConnected();
    await _connection!.invoke(
      'SendChatMessage',
      args: [bookingId, receiverId, message],
    );
  }

  Future<void> _ensureConnected() async {
    if (_connection?.state != HubConnectionState.Connected) {
      await connect();
    }
  }

  void _emitState() {
    final current = _connection?.state;
    if (current != null && !_stateController.isClosed) {
      _stateController.add(current);
    }
  }

  Future<void> dispose() async {
    final connection = _connection;
    _connection = null;
    if (connection?.state == HubConnectionState.Connected) {
      await connection!.stop();
    }
    await _stateController.close();
  }
}
