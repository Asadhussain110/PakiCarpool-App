import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../network/api_client.dart';

final signalRServiceProvider = Provider<SignalRService>((ref) {
  final service = SignalRService(ref.watch(apiClientProvider));
  ref.onDispose(service.dispose);
  return service;
});

final notificationInboxProvider =
    NotifierProvider<NotificationInboxController, List<Map<String, dynamic>>>(
      NotificationInboxController.new,
    );

class NotificationInboxController
    extends Notifier<List<Map<String, dynamic>>> {
  @override
  List<Map<String, dynamic>> build() => const [];

  void add(Map<String, dynamic> notification) {
    state = List.unmodifiable([
      ...state,
      {
        ...notification,
        'receivedAt': DateTime.now().toUtc().toIso8601String(),
      },
    ].skip(state.length >= 100 ? state.length - 99 : 0));
  }

  void clear() => state = const [];
}

class SignalRService {
  SignalRService(this._apiClient) {
    on('ReceiveNotification', _handleNotification);
    on('ReceivePushNotification', _handleNotification);
  }

  final ApiClient _apiClient;
  HubConnection? _connection;
  final Map<String, List<void Function(List<Object?>?)>> _handlers = {};
  final _stateController = StreamController<HubConnectionState>.broadcast();
  final _notificationController =
      StreamController<Map<String, dynamic>>.broadcast();

  Stream<HubConnectionState> get states => _stateController.stream;
  Stream<Map<String, dynamic>> get notifications =>
      _notificationController.stream;
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
    final hubUrl = _apiClient.baseUrl
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
    for (final entry in _handlers.entries) {
      for (final handler in entry.value) {
        connection.on(entry.key, handler);
      }
    }
    await connection.start();
    _emitState();
  }

  void on(String event, void Function(List<Object?>?) handler) {
    _handlers.putIfAbsent(event, () => []).add(handler);
    _connection?.on(event, handler);
  }

  void _handleNotification(List<Object?>? args) {
    if (args == null || args.isEmpty || _notificationController.isClosed) {
      return;
    }
    final payload = args.first;
    if (payload is Map) {
      _notificationController.add(
        payload.map<String, dynamic>(
          (key, value) => MapEntry(key.toString(), value),
        ),
      );
    }
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

  Future<void> leavePassengerRide(String rideId, String passengerId) async {
    final connection = _connection;
    if (connection?.state == HubConnectionState.Connected) {
      await connection!.invoke(
        'LeavePassengerRideGroup',
        args: [rideId, passengerId],
      );
    }
  }

  Future<void> leaveRide(String rideId) async {
    final connection = _connection;
    if (connection?.state == HubConnectionState.Connected) {
      await connection!.invoke('LeaveRideGroup', args: [rideId]);
    }
  }

  Future<void> stop() async {
    final connection = _connection;
    if (connection == null) return;
    try {
      if (connection.state != HubConnectionState.Disconnected) {
        await connection.stop();
      }
    } finally {
      if (identical(_connection, connection)) _connection = null;
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
    await stop();
    await _stateController.close();
    await _notificationController.close();
  }
}

String notificationDisplayMessage(Map<String, dynamic> notification) {
  final message = notification['message']?.toString().trim();
  if (message?.isNotEmpty == true) return message!;

  final title = notification['title']?.toString().trim();
  return switch (notification['type']?.toString()) {
    'RideCancelledByDriver' => 'The ride was cancelled by the driver.',
    'BookingAccepted' => 'Your booking was accepted.',
    'BookingRejected' => 'Your booking was rejected.',
    'DriverArrivedAtStop' => 'Your driver arrived at a stop.',
    _ => title?.isNotEmpty == true ? title! : 'You have a new notification.',
  };
}
