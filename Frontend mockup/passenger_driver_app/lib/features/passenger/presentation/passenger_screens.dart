import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/mobile_api.dart';
import '../../../core/realtime/signalr_service.dart';
import '../../shared/route_map.dart';
import '../../shared/screen_parts.dart';

class PassengerRideListScreen extends ConsumerStatefulWidget {
  const PassengerRideListScreen({super.key});

  @override
  ConsumerState<PassengerRideListScreen> createState() =>
      _PassengerRideListScreenState();
}

class _PassengerRideListScreenState
    extends ConsumerState<PassengerRideListScreen> {
  final List<Map<String, dynamic>> _rides = [];
  Map<String, dynamic>? _activeBooking;
  int _page = 1;
  int _totalPages = 1;
  bool _loading = true;
  bool _loadingMore = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({bool append = false}) async {
    if (append) {
      setState(() => _loadingMore = true);
    } else {
      setState(() {
        _loading = true;
        _error = null;
      });
    }
    try {
      final api = ref.read(mobileApiProvider);
      final page = append ? _page + 1 : 1;
      final result = await api.getData(
        'passenger/rides',
        query: {'page': page, 'pageSize': 10},
      );
      final items = result['items'];
      final newRides = items is List
          ? items.map(asMap).toList()
          : <Map<String, dynamic>>[];
      final active = await _loadActive(api);
      if (!mounted) return;
      setState(() {
        if (append) {
          _rides.addAll(newRides);
        } else {
          _rides
            ..clear()
            ..addAll(newRides);
        }
        _page = page;
        _totalPages = asInt(result['totalPages']);
        _activeBooking = active;
        _loading = false;
        _loadingMore = false;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _error = failure.message;
        _loading = false;
        _loadingMore = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _error =
            'Rides could not be loaded. Check your connection and try again.';
        _loading = false;
        _loadingMore = false;
      });
    }
  }

  Future<Map<String, dynamic>?> _loadActive(MobileApi api) async {
    try {
      return await api.getData('passenger/bookings/active');
    } on ApiFailure {
      return null;
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const LoadingPane(label: 'Finding rides');
    if (_error != null) return ErrorPane(message: _error!, onRetry: _load);
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            'Find a shared route',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 5),
          const Text('Choose a ride that follows your part of the way.'),
          if (_activeBooking != null) ...[
            const SizedBox(height: 18),
            Material(
              color: const Color(0xFFE1F2EF),
              borderRadius: BorderRadius.circular(8),
              child: ListTile(
                leading: const Icon(Icons.route, color: Color(0xFF087D85)),
                title: const Text('You have an active ride'),
                subtitle: Text(
                  '${_activeBooking!['pickupStopName'] ?? 'Pickup'} to ${_activeBooking!['dropoffStopName'] ?? 'Drop-off'}',
                ),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.push('/passenger/active'),
              ),
            ),
          ],
          const SizedBox(height: 20),
          if (_rides.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 64),
              child: Column(
                children: [
                  Icon(Icons.alt_route, size: 42),
                  SizedBox(height: 12),
                  Text('No posted rides yet'),
                  SizedBox(height: 4),
                  Text('Pull down to check again.'),
                ],
              ),
            )
          else
            for (final ride in _rides) ...[
              _RideListItem(
                ride: ride,
                onTap: () => context.push('/passenger/ride/${ride['id']}'),
              ),
              const Divider(height: 1),
            ],
          if (_page < _totalPages)
            Padding(
              padding: const EdgeInsets.only(top: 18),
              child: OutlinedButton(
                onPressed: _loadingMore ? null : () => _load(append: true),
                child: _loadingMore
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('Load more rides'),
              ),
            ),
          const SizedBox(height: 12),
          Text(
            'Routes and stop order come from driver posts. Segment availability is confirmed when you request a seat.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

class _RideListItem extends StatelessWidget {
  const _RideListItem({required this.ride, required this.onTap});

  final Map<String, dynamic> ride;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final stops = _stops(ride);
    final route = stops
        .map((stop) {
          final suffix = stop.etaSeconds == null
              ? ''
              : ' · ${_duration(stop.etaSeconds!)}';
          return '${stop.name}$suffix';
        })
        .join('  ›  ');
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 17),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Text(
                    (ride['driverName'] ?? 'Driver').toString(),
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                Text(
                  _money(ride['fare']),
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                    color: const Color(0xFF00666E),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              '${_rating(ride['driverRating'])}  ·  ${ride['carMake'] ?? ''} ${ride['carModel'] ?? ''}  ·  ${ride['maxSeats'] ?? '—'} seats',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 11),
            Text(route.isEmpty ? 'Stops unavailable' : route, maxLines: 2),
            const SizedBox(height: 7),
            Row(
              children: [
                StateLabel((ride['status'] ?? 'Posted').toString()),
                const Spacer(),
                const Text('View route'),
                const SizedBox(width: 2),
                const Icon(Icons.chevron_right, size: 18),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class PassengerRideDetailScreen extends ConsumerStatefulWidget {
  const PassengerRideDetailScreen({required this.rideId, super.key});

  final String rideId;

  @override
  ConsumerState<PassengerRideDetailScreen> createState() =>
      _PassengerRideDetailScreenState();
}

class _PassengerRideDetailScreenState
    extends ConsumerState<PassengerRideDetailScreen> {
  Map<String, dynamic>? _ride;
  String? _pickupId;
  String? _dropoffId;
  String? _error;
  bool _loading = true;
  bool _submitting = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final ride = await ref
          .read(mobileApiProvider)
          .getData('passenger/rides/${widget.rideId}');
      if (!mounted) return;
      setState(() {
        _ride = ride;
        _loading = false;
      });
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() {
          _error = failure.message;
          _loading = false;
        });
      }
    }
  }

  Future<void> _book() async {
    if (_pickupId == null || _dropoffId == null) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final result = await ref
          .read(mobileApiProvider)
          .postData(
            'passenger/bookings',
            body: {
              'rideId': widget.rideId,
              'pickupStopId': _pickupId,
              'dropoffStopId': _dropoffId,
            },
          );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text((result['status'] ?? 'Pending').toString())),
      );
      context.go('/passenger/active');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: LoadingPane(label: 'Loading route'));
    }
    if (_error != null && _ride == null) {
      return Scaffold(
        body: ErrorPane(message: _error!, onRetry: _load),
      );
    }
    final ride = _ride ?? const <String, dynamic>{};
    final stops = _stops(ride);
    final futureStops = stops.where((stop) => !stop.completed).toList();
    final pickup = futureStops
        .where((stop) => stop.id == _pickupId)
        .firstOrNull;
    final dropoffs = pickup == null
        ? const <RouteStopPoint>[]
        : futureStops.where((stop) => stop.sequence > pickup.sequence).toList();
    if (_dropoffId != null && !dropoffs.any((stop) => stop.id == _dropoffId)) {
      _dropoffId = null;
    }

    return Scaffold(
      appBar: AppBar(title: const Text('Ride route')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            '${ride['driverName'] ?? 'Driver'} · ${ride['carMake'] ?? ''} ${ride['carModel'] ?? ''}',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 6),
          Text(
            '${_rating(ride['driverRating'])}  ·  ${_money(ride['fare'])} per seat',
          ),
          const SizedBox(height: 18),
          RouteMap(stops: stops, height: 250),
          const SizedBox(height: 22),
          Text(
            'Choose your segment',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(
            initialValue: futureStops.any((stop) => stop.id == _pickupId)
                ? _pickupId
                : null,
            decoration: const InputDecoration(labelText: 'Pickup stop'),
            items: futureStops
                .map(
                  (stop) => DropdownMenuItem(
                    value: stop.id,
                    child: Text('${stop.sequence}. ${stop.name}'),
                  ),
                )
                .toList(),
            onChanged: (value) => setState(() {
              _pickupId = value;
              _dropoffId = null;
            }),
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<String>(
            initialValue: dropoffs.any((stop) => stop.id == _dropoffId)
                ? _dropoffId
                : null,
            decoration: const InputDecoration(labelText: 'Drop-off stop'),
            items: dropoffs
                .map(
                  (stop) => DropdownMenuItem(
                    value: stop.id,
                    child: Text('${stop.sequence}. ${stop.name}'),
                  ),
                )
                .toList(),
            onChanged: pickup == null
                ? null
                : (value) => setState(() => _dropoffId = value),
          ),
          const SizedBox(height: 10),
          const Text(
            'Passed stops are unavailable. Segment capacity is checked by the server when you request.',
            style: TextStyle(color: Color(0xFF44585D)),
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            NoticeStrip(message: _error!, isError: true),
          ],
          const SizedBox(height: 22),
          FilledButton(
            onPressed: _submitting || _pickupId == null || _dropoffId == null
                ? null
                : _book,
            child: _submitting
                ? const SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Book me'),
          ),
        ],
      ),
    );
  }
}

class ActiveBookingScreen extends ConsumerStatefulWidget {
  const ActiveBookingScreen({super.key});

  @override
  ConsumerState<ActiveBookingScreen> createState() =>
      _ActiveBookingScreenState();
}

class _ActiveBookingScreenState extends ConsumerState<ActiveBookingScreen> {
  Map<String, dynamic>? _booking;
  Map<String, dynamic>? _ride;
  LatLng? _driverLocation;
  String? _error;
  String? _eventNotice;
  Timer? _refreshTimer;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
    _refreshTimer = Timer.periodic(
      const Duration(seconds: 8),
      (_) => _load(silent: true),
    );
    _listenRealtime();
  }

  Future<void> _listenRealtime() async {
    final realtime = ref.read(signalRServiceProvider);
    realtime.on('ReceiveLocationUpdate', (args) {
      if (args == null || args.isEmpty) return;
      final payload = asMap(args.first);
      final lat = asDouble(payload['lat']);
      final lng = asDouble(payload['lng']);
      if (mounted) setState(() => _driverLocation = LatLng(lat, lng));
    });
    realtime.on('DriverArrivedAtStop', (args) {
      if (mounted) {
        setState(
          () => _eventNotice =
              'Driver arrived at ${_eventString(args, 'stopName')}.',
        );
      }
    });
    realtime.on('DriverIsHere', (args) {
      if (mounted) setState(() => _eventNotice = 'Your driver is here.');
    });
    realtime.on('RideEndedForPassenger', (args) {
      if (args == null || args.isEmpty || !mounted) return;
      final payload = asMap(args.first);
      final bookingId = payload['bookingId']?.toString();
      if (bookingId == _booking?['id']?.toString()) {
        setState(
          () => _eventNotice =
              'Ride complete. Please pay ${_money(payload['fare'])} in cash.',
        );
      }
    });
    realtime.on('CashReceivedConfirmed', (args) {
      if (args == null || args.isEmpty || !mounted) return;
      final payload = asMap(args.first);
      if (payload['bookingId']?.toString() == _booking?['id']?.toString()) {
        _openFeedback();
      }
    });
    realtime.on('RideCancelledByDriver', (args) {
      if (mounted) {
        setState(() => _eventNotice = 'This ride was cancelled by the driver.');
      }
    });
  }

  Future<void> _load({bool silent = false}) async {
    if (!silent && mounted) {
      setState(() {
        _loading = true;
        _error = null;
      });
    }
    try {
      final api = ref.read(mobileApiProvider);
      final booking = await api.getData('passenger/bookings/active');
      final rideId = booking['rideId']?.toString();
      Map<String, dynamic>? ride;
      if (rideId != null && rideId.isNotEmpty) {
        ride = await api.getData('passenger/rides/$rideId');
      }
      final lat = booking['driverLat'];
      final lng = booking['driverLng'];
      if (!mounted) return;
      setState(() {
        _booking = booking;
        _ride = ride;
        if (lat != null && lng != null) {
          _driverLocation = LatLng(asDouble(lat), asDouble(lng));
        }
        _loading = false;
        _error = null;
      });
      if (rideId != null) {
        final realtime = ref.read(signalRServiceProvider);
        await realtime.connect();
        await realtime.joinRide(rideId);
      }
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _error = failure.message;
        _loading = false;
        if (failure.statusCode == 404 ||
            failure.message.contains('No active booking')) {
          _booking = null;
        }
      });
    }
  }

  Future<void> _cancel() async {
    final bookingId = _booking?['id']?.toString();
    if (bookingId == null) return;
    final reason = await askForText(
      context,
      title: 'Cancel booking',
      label: 'Reason for cancellation',
      minLength: 10,
    );
    if (reason == null || !mounted) return;
    try {
      await ref
          .read(mobileApiProvider)
          .deleteData(
            'passenger/bookings/$bookingId',
            body: {'reason': reason},
          );
      if (mounted) {
        setState(() {
          _booking = null;
          _eventNotice = 'Booking cancelled.';
        });
      }
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    }
  }

  Future<void> _noShow() async {
    final bookingId = _booking?['id']?.toString();
    if (bookingId == null) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Report driver no-show?'),
        content: const Text(
          'Confirm only after you have waited at your pickup stop for at least 3 minutes.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Not yet'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Confirm report'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    try {
      await ref
          .read(mobileApiProvider)
          .putData('passenger/bookings/$bookingId/no-show');
      if (mounted) setState(() => _eventNotice = 'No-show report submitted.');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    }
  }

  Future<void> _openFeedback() async {
    final rideId = _booking?['rideId']?.toString();
    final rateeId = _ride?['driverId']?.toString();
    if (rideId == null || rateeId == null || !mounted) return;
    context.push('/feedback?rideId=$rideId&rateeId=$rateeId&role=Passenger');
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    final rideId = _booking?['rideId']?.toString();
    if (rideId != null) {
      unawaited(ref.read(signalRServiceProvider).leaveRide(rideId));
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: LoadingPane(label: 'Loading your booking'));
    }
    if (_booking == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Active booking')),
        body: ErrorPane(
          message: _error ?? 'You do not have an active booking.',
          onRetry: _load,
        ),
      );
    }
    final booking = _booking!;
    final status = (booking['status'] ?? 'Pending').toString();
    final stops = _stops(_ride ?? const {});
    final pickupId = stops
        .where((stop) => stop.name == booking['pickupStopName'])
        .firstOrNull
        ?.id;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Your ride'),
        actions: [
          if (status.toLowerCase() == 'active')
            IconButton(
              tooltip: 'Chat with driver',
              onPressed: () {
                final driverId = _ride?['driverId']?.toString() ?? '';
                context.push(
                  '/chat/${booking['id']}?receiverId=$driverId&title=${Uri.encodeQueryComponent((booking['driverName'] ?? 'Driver').toString())}',
                );
              },
              icon: const Icon(Icons.chat_bubble_outline),
            ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(18, 8, 18, 28),
        children: [
          if (_eventNotice != null) ...[
            NoticeStrip(message: _eventNotice!),
            const SizedBox(height: 12),
          ],
          RouteMap(
            stops: stops,
            driverLocation: _driverLocation,
            pickupStopId: pickupId,
            height: 390,
          ),
          const SizedBox(height: 17),
          Row(
            children: [
              Expanded(
                child: Text(
                  (booking['driverName'] ?? 'Driver').toString(),
                  style: Theme.of(context).textTheme.titleLarge,
                ),
              ),
              StateLabel(status),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            '${booking['carMake'] ?? ''} ${booking['carModel'] ?? ''}  ·  ${_money(booking['fare'])} per seat',
          ),
          const SizedBox(height: 14),
          _RouteSummaryLine(
            label: 'Pickup',
            value: (booking['pickupStopName'] ?? '—').toString(),
          ),
          _RouteSummaryLine(
            label: 'Drop-off',
            value: (booking['dropoffStopName'] ?? '—').toString(),
          ),
          _RouteSummaryLine(
            label: 'Driver ETA',
            value: booking['etaSeconds'] == null
                ? 'Waiting for location'
                : _duration(asInt(booking['etaSeconds'])),
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            NoticeStrip(message: _error!, isError: true),
          ],
          if (status.toLowerCase() == 'completed') ...[
            const SizedBox(height: 12),
            const NoticeStrip(
              message: 'Please pay the driver in cash. The rating step opens after the driver confirms receipt.',
            ),
          ],
          const SizedBox(height: 16),
          if (status.toLowerCase() == 'pending')
            const Text(
              'Waiting for the driver to accept. Requests expire after two minutes.',
            ),
          if (status.toLowerCase() == 'accepted' ||
              status.toLowerCase() == 'active' ||
              status.toLowerCase() == 'pending') ...[
            OutlinedButton.icon(
              onPressed: _noShow,
              icon: const Icon(Icons.report_gmailerrorred_outlined),
              label: const Text('Driver has not arrived'),
            ),
            TextButton(onPressed: _cancel, child: const Text('Cancel booking')),
          ],
        ],
      ),
    );
  }
}

class TripFeedbackScreen extends ConsumerStatefulWidget {
  const TripFeedbackScreen({
    required this.rideId,
    required this.rateeId,
    required this.role,
    super.key,
  });

  final String rideId;
  final String rateeId;
  final String role;

  @override
  ConsumerState<TripFeedbackScreen> createState() => _TripFeedbackScreenState();
}

class _TripFeedbackScreenState extends ConsumerState<TripFeedbackScreen> {
  final _review = TextEditingController();
  final _flagReason = TextEditingController();
  int _stars = 5;
  bool _submitting = false;
  String? _notice;
  String? _error;
  bool _flagMode = false;

  @override
  void dispose() {
    _review.dispose();
    _flagReason.dispose();
    super.dispose();
  }

  Future<void> _submitRating() async {
    setState(() {
      _submitting = true;
      _error = null;
    });
    final prefix = widget.role == 'Driver' ? 'driver' : 'passenger';
    try {
      await ref
          .read(mobileApiProvider)
          .postData(
            '$prefix/ratings',
            body: {
              'rideId': widget.rideId,
              'rateeId': widget.rateeId,
              'stars': _stars,
              'reviewText': _review.text.trim().isEmpty
                  ? null
                  : _review.text.trim(),
            },
          );
      if (mounted) {
        setState(
          () => _notice = 'Rating submitted. Thanks for closing the loop.',
        );
      }
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  Future<void> _submitFlag() async {
    if (_flagReason.text.trim().length < 10) {
      setState(() => _error = 'Please enter at least 10 characters.');
      return;
    }
    setState(() {
      _submitting = true;
      _error = null;
    });
    final prefix = widget.role == 'Driver' ? 'driver' : 'passenger';
    try {
      await ref
          .read(mobileApiProvider)
          .postData(
            '$prefix/flags',
            body: {
              'reporteeId': widget.rateeId,
              'rideId': widget.rideId,
              'reason': _flagReason.text.trim(),
            },
          );
      if (mounted) setState(() => _notice = 'Report submitted for review.');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(_flagMode ? 'Report a concern' : 'Rate your ride'),
    ),
    body: ListView(
      padding: const EdgeInsets.all(22),
      children: [
        Text(
          _flagMode ? 'Tell us what happened' : 'How was your trip?',
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 16),
        if (_flagMode)
          TextField(
            controller: _flagReason,
            minLines: 4,
            maxLines: 7,
            maxLength: 500,
            decoration: const InputDecoration(
              labelText: 'Reason (10–500 characters)',
            ),
          )
        else ...[
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              for (var index = 1; index <= 5; index++)
                IconButton(
                  tooltip: '$index stars',
                  onPressed: () => setState(() => _stars = index),
                  icon: Icon(
                    index <= _stars ? Icons.star : Icons.star_border,
                    size: 34,
                  ),
                  color: const Color(0xFF9B7B00),
                ),
            ],
          ),
          TextField(
            controller: _review,
            minLines: 3,
            maxLines: 5,
            maxLength: 300,
            decoration: const InputDecoration(labelText: 'Review (optional)'),
          ),
        ],
        if (_error != null) ...[
          const SizedBox(height: 12),
          NoticeStrip(message: _error!, isError: true),
        ],
        if (_notice != null) ...[
          const SizedBox(height: 12),
          NoticeStrip(message: _notice!),
        ],
        const SizedBox(height: 18),
        FilledButton(
          onPressed: _submitting
              ? null
              : (_flagMode ? _submitFlag : _submitRating),
          child: _submitting
              ? const SizedBox.square(
                  dimension: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Text(_flagMode ? 'Submit report' : 'Submit rating'),
        ),
        TextButton(
          onPressed: () => setState(() => _flagMode = !_flagMode),
          child: Text(
            _flagMode ? 'Back to rating' : 'Report a concern instead',
          ),
        ),
        TextButton(
          onPressed: () => context.go('/home'),
          child: const Text('Skip and return home'),
        ),
      ],
    ),
  );
}

class _RouteSummaryLine extends StatelessWidget {
  const _RouteSummaryLine({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 5),
    child: Row(
      children: [
        SizedBox(
          width: 92,
          child: Text(label, style: Theme.of(context).textTheme.bodySmall),
        ),
        Expanded(
          child: Text(
            value,
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
        ),
      ],
    ),
  );
}

List<RouteStopPoint> _stops(Map<String, dynamic> ride) {
  final value = ride['stops'];
  if (value is! List) return const [];
  return value.map((item) => RouteStopPoint.fromJson(asMap(item))).toList()
    ..sort((a, b) => a.sequence.compareTo(b.sequence));
}

String _money(Object? value) => 'PKR ${asDouble(value).toStringAsFixed(0)}';

String _rating(Object? value) => value == null || asDouble(value) == 0
    ? 'New driver'
    : '${asDouble(value).toStringAsFixed(1)} rating';

String _duration(int seconds) {
  final minutes = (seconds / 60).ceil();
  return minutes < 1 ? 'Arriving now' : '$minutes min';
}

String _eventString(List<Object?>? args, String key) {
  if (args == null || args.isEmpty) return 'your stop';
  return asMap(args.first)[key]?.toString() ?? 'your stop';
}
