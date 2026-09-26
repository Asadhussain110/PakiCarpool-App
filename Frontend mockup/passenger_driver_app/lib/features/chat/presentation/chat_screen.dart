import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/mobile_api.dart';
import '../../../core/realtime/signalr_service.dart';
import '../../../core/theme/route_atlas_theme.dart';
import '../../auth/models/app_session.dart';
import '../../shared/screen_parts.dart';

class BookingChatScreen extends ConsumerStatefulWidget {
  const BookingChatScreen({
    required this.bookingId,
    required this.receiverId,
    required this.title,
    super.key,
  });

  final String bookingId;
  final String receiverId;
  final String title;

  @override
  ConsumerState<BookingChatScreen> createState() => _BookingChatScreenState();
}

class _BookingChatScreenState extends ConsumerState<BookingChatScreen> {
  final _composer = TextEditingController();
  final _scrollController = ScrollController();
  final List<Map<String, dynamic>> _messages = [];
  final List<String> _queued = [];
  StreamSubscription<HubConnectionState>? _stateSubscription;
  bool _loading = true;
  bool _sending = false;
  HubConnectionState? _connectionState;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadHistory();
    _connect();
  }

  Future<void> _loadHistory() async {
    try {
      final data = await ref
          .read(mobileApiProvider)
          .getData('chat/${widget.bookingId}/messages');
      final messages = data['messages'];
      if (!mounted) return;
      setState(() {
        _messages
          ..clear()
          ..addAll(messages is List ? messages.map(asMap) : const []);
        _loading = false;
      });
      _scrollToLatest();
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() {
          _error = failure.message;
          _loading = false;
        });
      }
    }
  }

  Future<void> _connect() async {
    final realtime = ref.read(signalRServiceProvider);
    realtime.on('ReceiveChatMessage', _receiveMessage);
    _stateSubscription = realtime.states.listen((state) {
      if (!mounted) return;
      setState(() => _connectionState = state);
      if (state == HubConnectionState.Connected) unawaited(_retryQueued());
    });
    try {
      await realtime.connect();
      if (mounted) setState(() => _connectionState = realtime.state);
    } catch (_) {
      if (mounted) {
        setState(
          () => _error =
              'Chat is reconnecting. Messages can be retried when online.',
        );
      }
    }
  }

  void _receiveMessage(List<Object?>? args) {
    if (args == null || args.isEmpty || !mounted) return;
    final message = asMap(args.first);
    if (message['bookingId']?.toString() != widget.bookingId) return;
    setState(() => _messages.add(message));
    _scrollToLatest();
  }

  Future<void> _send() async {
    final text = _composer.text.trim();
    if (text.isEmpty || _sending) return;
    if (text.length > 1000) {
      setState(() => _error = 'Messages can contain up to 1,000 characters.');
      return;
    }
    _composer.clear();
    await _sendMessage(text, queueOnFailure: true);
  }

  Future<void> _sendMessage(String text, {required bool queueOnFailure}) async {
    final session = ref.read(appSessionProvider);
    if (session == null) return;
    setState(() {
      _sending = true;
      _error = null;
    });
    try {
      await ref
          .read(signalRServiceProvider)
          .sendChat(
            bookingId: widget.bookingId,
            receiverId: widget.receiverId,
            message: text,
          );
      if (!mounted) return;
      setState(() {
        _messages.add({
          'bookingId': widget.bookingId,
          'senderId': session.userId,
          'message': text,
          'sentAt': DateTime.now().toUtc().toIso8601String(),
          'isDelivered': true,
        });
      });
      _scrollToLatest();
    } catch (_) {
      if (!mounted) return;
      if (queueOnFailure) {
        setState(() {
          _queued.add(text);
          _error = 'Reconnecting. Your message is queued on this device.';
        });
      } else {
        setState(() => _queued.insert(0, text));
      }
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _retryQueued() async {
    while (_queued.isNotEmpty && mounted) {
      final next = _queued.first;
      try {
        final session = ref.read(appSessionProvider);
        if (session == null) return;
        await ref
            .read(signalRServiceProvider)
            .sendChat(
              bookingId: widget.bookingId,
              receiverId: widget.receiverId,
              message: next,
            );
        if (!mounted) return;
        setState(() {
          _queued.removeAt(0);
          _messages.add({
            'bookingId': widget.bookingId,
            'senderId': session.userId,
            'message': next,
            'sentAt': DateTime.now().toUtc().toIso8601String(),
            'isDelivered': true,
          });
          _error = null;
        });
      } catch (_) {
        return;
      }
    }
    _scrollToLatest();
  }

  void _scrollToLatest() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scrollController.hasClients) {
        _scrollController.animateTo(
          _scrollController.position.maxScrollExtent,
          duration: const Duration(milliseconds: 180),
          curve: Curves.easeOut,
        );
      }
    });
  }

  @override
  void dispose() {
    _stateSubscription?.cancel();
    _composer.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(appSessionProvider);
    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(widget.title),
            Text(
              _queued.isNotEmpty
                  ? 'Reconnecting · ${_queued.length} queued'
                  : _connectionState == HubConnectionState.Connected
                  ? 'Connected'
                  : 'Reconnecting…',
              style: Theme.of(context).textTheme.labelSmall,
            ),
          ],
        ),
      ),
      body: Column(
        children: [
          if (_error != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 8, 14, 0),
              child: NoticeStrip(message: _error!, isError: _queued.isEmpty),
            ),
          Expanded(
            child: _loading
                ? const LoadingPane(label: 'Loading chat history')
                : ListView.builder(
                    controller: _scrollController,
                    padding: const EdgeInsets.fromLTRB(14, 18, 14, 18),
                    itemCount: _messages.length,
                    itemBuilder: (context, index) {
                      final message = _messages[index];
                      final mine =
                          message['senderId']?.toString() == session?.userId;
                      return Align(
                        alignment: mine
                            ? Alignment.centerRight
                            : Alignment.centerLeft,
                        child: ConstrainedBox(
                          constraints: BoxConstraints(
                            maxWidth: MediaQuery.sizeOf(context).width * .78,
                          ),
                          child: Container(
                            margin: const EdgeInsets.symmetric(vertical: 4),
                            padding: const EdgeInsets.symmetric(
                              horizontal: 14,
                              vertical: 10,
                            ),
                            decoration: BoxDecoration(
                              color: mine
                                  ? RouteAtlasColors.tealSurface
                                  : Colors.white,
                              borderRadius: BorderRadius.circular(14),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.end,
                              children: [
                                Text((message['message'] ?? '').toString()),
                                const SizedBox(height: 3),
                                Text(
                                  _time(message['sentAt']),
                                  style: Theme.of(context).textTheme.labelSmall,
                                ),
                              ],
                            ),
                          ),
                        ),
                      );
                    },
                  ),
          ),
          SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _composer,
                      maxLength: 1000,
                      minLines: 1,
                      maxLines: 4,
                      textInputAction: TextInputAction.send,
                      onSubmitted: (_) => _send(),
                      decoration: const InputDecoration(
                        hintText: 'Message your ride partner',
                        counterText: '',
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  IconButton.filled(
                    tooltip: 'Send message',
                    onPressed: _sending ? null : _send,
                    icon: _sending
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.send),
                  ),
                ],
              ),
            ),
          ),
          const Padding(
            padding: EdgeInsets.only(bottom: 8),
            child: Text(
              'Text only · history expires after 7 days',
              style: TextStyle(color: RouteAtlasColors.secondaryMuted),
            ),
          ),
        ],
      ),
    );
  }
}

String _time(Object? value) {
  final date = DateTime.tryParse(value?.toString() ?? '')?.toLocal();
  if (date == null) return '';
  final hour = date.hour % 12 == 0 ? 12 : date.hour % 12;
  final minute = date.minute.toString().padLeft(2, '0');
  return '$hour:$minute';
}
