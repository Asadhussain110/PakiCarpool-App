import 'dart:async';
import 'dart:math';

import 'package:camera/camera.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';
import 'package:latlong2/latlong.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/mobile_api.dart';
import '../../../core/realtime/signalr_service.dart';
import '../../../core/theme/route_atlas_theme.dart';
import '../../auth/models/app_session.dart';
import '../../shared/route_map.dart';
import '../../shared/screen_parts.dart';

class DriverDashboardScreen extends ConsumerStatefulWidget {
  const DriverDashboardScreen({super.key});

  @override
  ConsumerState<DriverDashboardScreen> createState() =>
      _DriverDashboardScreenState();
}

class _DriverDashboardScreenState extends ConsumerState<DriverDashboardScreen> {
  List<Map<String, dynamic>> _cars = const [];
  Map<String, dynamic>? _ride;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    final api = ref.read(mobileApiProvider);
    try {
      final cars = await api.getListData('driver/car/status');
      Map<String, dynamic>? ride;
      try {
        ride = await api.getData('driver/rides/active');
      } on ApiFailure catch (failure) {
        if (failure.statusCode != 400 && failure.statusCode != 404) rethrow;
      }
      if (!mounted) return;
      setState(() {
        _cars = cars;
        _ride = ride;
        _loading = false;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      if (failure.statusCode == 400 || failure.statusCode == 404) {
        setState(() {
          _cars = [];
          _ride = null;
          _loading = false;
        });
      } else {
        setState(() {
          _error = failure.message;
          _loading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(appSessionProvider);
    if (_loading) return const LoadingPane(label: 'Checking driver readiness');
    if (_error != null) return ErrorPane(message: _error!, onRetry: _load);

    final pendingLiveness = session?.livenessPending ?? false;
    final approved = _cars.any(
      (car) => '${car['status']}'.toLowerCase() == 'approved',
    );
    final hasRide =
        _ride != null &&
        '${_ride!['status']}'.toLowerCase() != 'completed' &&
        '${_ride!['status']}'.toLowerCase() != 'cancelled';
    final pendingCar = _cars.any(
      (car) => '${car['status']}'.toLowerCase() == 'pending',
    );

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            'Your route workbench',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 5),
          const Text('Clear each readiness check before you publish a route.'),
          const SizedBox(height: 20),
          if (pendingLiveness) ...[
            const _ReadinessLine(
              icon: Icons.face_retouching_natural,
              title: 'Liveness check required',
              detail: 'A check is requested for this login. Verification is not submitted until the face model is available.',
              state: 'Needs action',
              stateColor: RouteAtlasColors.coral,
            ),
            const SizedBox(height: 10),
            OutlinedButton.icon(
              onPressed: () => context.push('/driver/liveness'),
              icon: const Icon(Icons.camera_alt_outlined),
              label: const Text('Open liveness check'),
            ),
          ] else
            const _ReadinessLine(
              icon: Icons.verified_user_outlined,
              title: 'Liveness',
              detail:
                  'No additional check was requested by this login response.',
              state: 'Ready',
            ),
          const SizedBox(height: 16),
          _ReadinessLine(
            icon: Icons.directions_car_outlined,
            title: 'Approved car',
            detail: approved
                ? '${_approvedCar(_cars)?['make']} ${_approvedCar(_cars)?['model']} is approved.'
                : pendingCar
                ? 'Your vehicle is waiting for Admin review.'
                : 'Register your car and documents for review.',
            state: approved
                ? 'Ready'
                : pendingCar
                ? 'Pending'
                : 'Needs action',
            stateColor: approved
                ? RouteAtlasColors.teal
                : RouteAtlasColors.coral,
          ),
          const SizedBox(height: 10),
          OutlinedButton.icon(
            onPressed: () => context.push('/driver/car'),
            icon: const Icon(Icons.directions_car_filled_outlined),
            label: Text(approved ? 'View car status' : 'Register a car'),
          ),
          const SizedBox(height: 20),
          if (hasRide) ...[
            _ReadinessLine(
              icon: Icons.alt_route,
              title: 'Current ride',
              detail:
                  '${(_ride!['stops'] as List? ?? const []).length} ordered stops · ${_ride!['status']}',
              state: 'Resume',
            ),
            const SizedBox(height: 10),
            FilledButton.icon(
              onPressed: () => context.push('/driver/ride/active'),
              icon: const Icon(Icons.route),
              label: const Text('Open active ride'),
            ),
          ] else ...[
            const _ReadinessLine(
              icon: Icons.add_road,
              title: 'Post a ride',
              detail: 'Publish ordered stops and a fare per seat.',
              state: 'One ride at a time',
            ),
            const SizedBox(height: 10),
            FilledButton.icon(
              onPressed: approved && !pendingLiveness
                  ? () => context.push('/driver/ride/new')
                  : null,
              icon: const Icon(Icons.add_location_alt_outlined),
              label: const Text('Build a route'),
            ),
          ],
          if (_cars.isNotEmpty) ...[
            const SizedBox(height: 28),
            Text(
              'Vehicle submissions',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 6),
            for (final car in _cars)
              ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text('${car['make'] ?? ''} ${car['model'] ?? ''}'),
                subtitle: Text(
                  car['rejectionReason']?.toString() ??
                      'Year ${car['year'] ?? '—'}',
                ),
                trailing: StateLabel(
                  '${car['status'] ?? 'Unknown'}',
                  color: '${car['status']}'.toLowerCase() == 'approved'
                      ? RouteAtlasColors.teal
                      : RouteAtlasColors.secondaryMuted,
                ),
              ),
          ],
        ],
      ),
    );
  }
}

class CarRegistrationScreen extends ConsumerStatefulWidget {
  const CarRegistrationScreen({super.key});

  @override
  ConsumerState<CarRegistrationScreen> createState() =>
      _CarRegistrationScreenState();
}

class _CarRegistrationScreenState extends ConsumerState<CarRegistrationScreen> {
  final _formKey = GlobalKey<FormState>();
  final _make = TextEditingController();
  final _model = TextEditingController();
  final _year = TextEditingController();
  final _plate = TextEditingController();
  final _picker = ImagePicker();
  List<XFile> _carPhotos = [];
  XFile? _registrationPhoto;
  List<Map<String, dynamic>> _statuses = const [];
  int _seats = 3;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadStatuses();
  }

  @override
  void dispose() {
    _make.dispose();
    _model.dispose();
    _year.dispose();
    _plate.dispose();
    super.dispose();
  }

  Future<void> _loadStatuses() async {
    try {
      final statuses = await ref
          .read(mobileApiProvider)
          .getListData('driver/car/status');
      if (!mounted) return;
      setState(() {
        _statuses = statuses;
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

  Future<void> _pickCarPhotos() async {
    final images = await _picker.pickMultiImage(imageQuality: 85);
    if (mounted && images.isNotEmpty) {
      setState(() => _carPhotos = images.take(6).toList());
    }
  }

  Future<void> _pickRegistrationPhoto() async {
    final image = await _picker.pickImage(
      source: ImageSource.gallery,
      imageQuality: 85,
    );
    if (mounted && image != null) setState(() => _registrationPhoto = image);
  }

  Widget _textField(
    TextEditingController controller,
    String label, {
    required bool required,
    int? maxLength,
    TextInputType keyboard = TextInputType.text,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 14),
    child: TextFormField(
      controller: controller,
      keyboardType: keyboard,
      maxLength: maxLength,
      validator: (value) {
        final text = value?.trim() ?? '';
        if (required && text.isEmpty) return 'Enter $label.';
        if (label == 'Year') {
          final year = int.tryParse(text);
          if (year == null || year < 2000 || year > 2030) {
            return 'Enter a year from 2000 to 2030.';
          }
        }
        return null;
      },
      decoration: InputDecoration(labelText: label),
    ),
  );

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Car approval')),
    body: _loading
        ? const LoadingPane(label: 'Loading vehicle status')
        : ListView(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
            children: [
              Text(
                'Register your car',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const SizedBox(height: 5),
              const Text(
                'An approved vehicle is required before posting a ride.',
              ),
              const SizedBox(height: 18),
              for (final car in _statuses)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: Text(
                    '${car['make'] ?? ''} ${car['model'] ?? ''} · ${car['year'] ?? ''}',
                  ),
                  subtitle: Text(
                    car['rejectionReason']?.toString() ?? 'Approval status',
                  ),
                  trailing: StateLabel('${car['status'] ?? 'Unknown'}'),
                ),
              if (_error != null) NoticeStrip(message: _error!, isError: true),
              const SizedBox(height: 12),
              Form(
                key: _formKey,
                child: Column(
                  children: [
                    _textField(_make, 'Make', required: true, maxLength: 50),
                    _textField(_model, 'Model', required: true, maxLength: 50),
                    _textField(
                      _year,
                      'Year',
                      required: true,
                      keyboard: TextInputType.number,
                    ),
                    _textField(
                      _plate,
                      'License plate',
                      required: true,
                      maxLength: 20,
                    ),
                    const SizedBox(height: 8),
                    Row(
                      children: [
                        const Expanded(child: Text('Available seats')),
                        IconButton(
                          tooltip: 'Remove one seat',
                          onPressed: _seats > 1
                              ? () => setState(() => _seats--)
                              : null,
                          icon: const Icon(Icons.remove_circle_outline),
                        ),
                        Text(
                          '$_seats',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        IconButton(
                          tooltip: 'Add one seat',
                          onPressed: _seats < 6
                              ? () => setState(() => _seats++)
                              : null,
                          icon: const Icon(Icons.add_circle_outline),
                        ),
                      ],
                    ),
                    const Divider(height: 24),
                    OutlinedButton.icon(
                      onPressed: _pickCarPhotos,
                      icon: const Icon(Icons.photo_library_outlined),
                      label: Text(
                        'Choose exterior photos (${_carPhotos.length})',
                      ),
                    ),
                    if (_carPhotos.isNotEmpty)
                      Text(
                        '${_carPhotos.length} photos selected. FRD/FSD require at least two.',
                      ),
                    OutlinedButton.icon(
                      onPressed: _pickRegistrationPhoto,
                      icon: const Icon(Icons.description_outlined),
                      label: Text(
                        _registrationPhoto == null
                            ? 'Choose registration document'
                            : 'Document selected',
                      ),
                    ),
                    const SizedBox(height: 12),
                    const NoticeStrip(
                      message: 'Submission is unavailable: the API currently accepts file paths but has no secure upload endpoint. Your selected files have not been uploaded or sent.',
                    ),
                    const SizedBox(height: 12),
                    FilledButton(
                      onPressed: null,
                      child: const Text('Submit for approval'),
                    ),
                  ],
                ),
              ),
            ],
          ),
  );
}

class PostRideScreen extends ConsumerStatefulWidget {
  const PostRideScreen({super.key});

  @override
  ConsumerState<PostRideScreen> createState() => _PostRideScreenState();
}

class _PostRideScreenState extends ConsumerState<PostRideScreen> {
  final _fare = TextEditingController();
  final List<_EditableStop> _stops = [];
  int _maxSeats = 3;
  bool _posting = false;
  String? _error;

  @override
  void dispose() {
    _fare.dispose();
    for (final stop in _stops) {
      stop.name.dispose();
    }
    super.dispose();
  }

  void _addStop([LatLng? point]) {
    setState(
      () => _stops.add(
        _EditableStop(
          name: TextEditingController(text: 'Stop ${_stops.length + 1}'),
          latitude: point?.latitude,
          longitude: point?.longitude,
        ),
      ),
    );
  }

  Future<void> _postRide() async {
    if (_stops.length < 2 ||
        _stops.any((stop) => stop.latitude == null || stop.longitude == null)) {
      setState(
        () => _error = 'Add at least two stops and place each stop on the map.',
      );
      return;
    }
    final fare = double.tryParse(_fare.text);
    if (fare == null || fare <= 0 || fare > 100000) {
      setState(
        () => _error = 'Enter a fare between PKR 1 and PKR 100,000 per seat.',
      );
      return;
    }
    setState(() {
      _posting = true;
      _error = null;
    });
    try {
      await ref
          .read(mobileApiProvider)
          .postData(
            'driver/rides',
            body: {
              'stops': [
                for (var index = 0; index < _stops.length; index++)
                  {
                    'name': _stops[index].name.text.trim(),
                    'lat': _stops[index].latitude,
                    'lng': _stops[index].longitude,
                    'sequenceOrder': index + 1,
                  },
              ],
              'fare': fare,
              'maxSeats': _maxSeats,
            },
          );
      if (mounted) context.go('/driver/ride/active');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    } finally {
      if (mounted) setState(() => _posting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final points = [
      for (var index = 0; index < _stops.length; index++)
        if (_stops[index].latitude != null && _stops[index].longitude != null)
          RouteStopPoint(
            id: '$index',
            name: _stops[index].name.text,
            sequence: index + 1,
            latitude: _stops[index].latitude!,
            longitude: _stops[index].longitude!,
          ),
    ];
    return Scaffold(
      appBar: AppBar(title: const Text('Build a route')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            'Put your route in order',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 5),
          const Text(
            'Tap the map to add a stop, then name it. At least two stops are required.',
          ),
          const SizedBox(height: 14),
          RouteMap(stops: points, onMapTap: _addStop, height: 300),
          const SizedBox(height: 12),
          ReorderableListView.builder(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            itemCount: _stops.length,
            onReorderItem: (oldIndex, newIndex) {
              setState(() {
                final stop = _stops.removeAt(oldIndex);
                _stops.insert(newIndex, stop);
              });
            },
            itemBuilder: (context, index) {
              final stop = _stops[index];
              return Row(
                key: ObjectKey(stop),
                children: [
                  SizedBox(width: 34, child: Text('${index + 1}.')),
                  const Icon(Icons.drag_handle),
                  Expanded(
                    child: TextField(
                      controller: stop.name,
                      decoration: InputDecoration(
                        hintText: 'Stop name',
                        helperText: stop.latitude == null
                            ? 'Tap the map to set coordinates'
                            : '${stop.latitude!.toStringAsFixed(5)}, ${stop.longitude!.toStringAsFixed(5)}',
                      ),
                    ),
                  ),
                  IconButton(
                    tooltip: 'Remove stop',
                    onPressed: () => setState(() => _stops.removeAt(index)),
                    icon: const Icon(Icons.remove_circle_outline),
                  ),
                ],
              );
            },
          ),
          TextButton.icon(
            onPressed: _addStop,
            icon: const Icon(Icons.add),
            label: const Text('Add named stop'),
          ),
          TextField(
            controller: _fare,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(labelText: 'Fare per seat (PKR)'),
          ),
          Row(
            children: [
              const Expanded(child: Text('Maximum seats')),
              IconButton(
                tooltip: 'Reduce seats',
                onPressed: _maxSeats > 1
                    ? () => setState(() => _maxSeats--)
                    : null,
                icon: const Icon(Icons.remove_circle_outline),
              ),
              Text('$_maxSeats'),
              IconButton(
                tooltip: 'Add seat',
                onPressed: _maxSeats < 6
                    ? () => setState(() => _maxSeats++)
                    : null,
                icon: const Icon(Icons.add_circle_outline),
              ),
            ],
          ),
          const Text(
            'The API verifies that this does not exceed approved vehicle capacity.',
          ),
          const SizedBox(height: 14),
          if (_error != null) NoticeStrip(message: _error!, isError: true),
          FilledButton(
            onPressed: _posting ? null : _postRide,
            child: _posting
                ? const SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Post ride'),
          ),
        ],
      ),
    );
  }
}

class ActiveRideScreen extends ConsumerStatefulWidget {
  const ActiveRideScreen({super.key});

  @override
  ConsumerState<ActiveRideScreen> createState() => _ActiveRideScreenState();
}

class _ActiveRideScreenState extends ConsumerState<ActiveRideScreen> {
  Map<String, dynamic>? _ride;
  List<Map<String, dynamic>> _bookings = const [];
  bool _loading = true;
  bool _sharingLocation = false;
  String? _error;
  Timer? _locationTimer;
  bool _locating = false;
  bool _realtimeListening = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final api = ref.read(mobileApiProvider);
      final ride = await api.getData('driver/rides/active');
      final bookings = await api.getData('driver/rides/${ride['id']}/bookings');
      final data = bookings['data'];
      if (!mounted) return;
      setState(() {
        _ride = ride;
        _bookings = data is List ? data.map(asMap).toList() : const [];
        _loading = false;
      });
      if ('${ride['status']}'.toLowerCase() == 'active') {
        await _startLocationSharing();
      }
      _listenForBookingRequests();
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _error = failure.message;
        _loading = false;
      });
    }
  }

  void _listenForBookingRequests() {
    if (_realtimeListening) return;
    _realtimeListening = true;
    final realtime = ref.read(signalRServiceProvider);
    realtime.on('BookingRequestReceived', (_) {
      unawaited(_load());
    });
    unawaited(realtime.connect());
  }

  Future<void> _startLocationSharing() async {
    if (_sharingLocation) return;
    final enabled = await Geolocator.isLocationServiceEnabled();
    if (!enabled) {
      if (mounted) {
        setState(
          () =>
              _error = 'Turn on location services to share your ride position.',
        );
      }
      return;
    }
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }
    if (permission == LocationPermission.denied ||
        permission == LocationPermission.deniedForever) {
      if (mounted) {
        setState(
          () =>
              _error = 'Location permission is needed for live ride tracking.',
        );
      }
      return;
    }
    try {
      final realtime = ref.read(signalRServiceProvider);
      await realtime.connect();
      _locationTimer?.cancel();
      _locationTimer = Timer.periodic(const Duration(seconds: 5), (_) async {
        if (_locating || _ride == null) return;
        _locating = true;
        try {
          final position = await Geolocator.getCurrentPosition();
          await realtime.sendLocation(
            _ride!['id'].toString(),
            position.latitude,
            position.longitude,
          );
        } catch (_) {
          if (mounted) {
            setState(
              () => _error = 'Live location paused. Check GPS and connection.',
            );
          }
        } finally {
          _locating = false;
        }
      });
      if (mounted) setState(() => _sharingLocation = true);
    } catch (_) {
      if (mounted) {
        setState(
          () => _error = 'Could not connect to live tracking. Try again.',
        );
      }
    }
  }

  Future<void> _bookingAction(String id, String action) async {
    if (action == 'no-show') {
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Mark passenger no-show?'),
          content: const Text(
            'Confirm only after waiting at the pickup stop for at least 3 minutes.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Not yet'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Confirm no-show'),
            ),
          ],
        ),
      );
      if (confirmed != true || !mounted) return;
    }
    try {
      await ref.read(mobileApiProvider).putData('driver/bookings/$id/$action');
      if (action == 'cash-received') {
        final passengerId = _bookings
            .where((booking) => booking['id']?.toString() == id)
            .firstOrNull?['passengerId']
            ?.toString();
        final rideId = _ride?['id']?.toString();
        if (passengerId != null && rideId != null && mounted) {
          context.push(
            '/feedback?rideId=$rideId&rateeId=$passengerId&role=Driver',
          );
        }
      }
      await _load();
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    }
  }

  Future<void> _cancelRide() async {
    final rideId = _ride?['id']?.toString();
    if (rideId == null) return;
    final reason = await askForText(
      context,
      title: 'Cancel this ride?',
      label: 'Cancellation reason',
      minLength: 10,
    );
    if (reason == null || !mounted) return;
    try {
      await ref
          .read(mobileApiProvider)
          .deleteData('driver/rides/$rideId', body: {'reason': reason});
      _locationTimer?.cancel();
      if (mounted) context.go('/home');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    }
  }

  Future<void> _navigateToNextStop() async {
    final stops = _stops(_ride ?? const {});
    final next = stops.where((stop) => !stop.completed).firstOrNull;
    if (next == null) return;
    final uri = Uri.parse(
      'https://maps.google.com/?daddr=${next.latitude},${next.longitude}',
    );
    if (!await launchUrl(uri, mode: LaunchMode.externalApplication) &&
        mounted) {
      setState(() => _error = 'Could not open Google Maps on this device.');
    }
  }

  Future<void> _arrive(RouteStopPoint stop) async {
    try {
      await ref
          .read(mobileApiProvider)
          .putData('driver/stops/${stop.id}/arrive');
      await _load();
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    }
  }

  @override
  void dispose() {
    _locationTimer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const LoadingPane(label: 'Loading active ride');
    if (_ride == null) {
      return ErrorPane(message: _error ?? 'No active ride.', onRetry: _load);
    }
    final stops = _stops(_ride!);
    final nextStop = stops.where((stop) => !stop.completed).firstOrNull;
    final status = (_ride!['status'] ?? 'Posted').toString();
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(18, 8, 18, 28),
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Ride progress',
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
              ),
              StateLabel(status),
            ],
          ),
          const SizedBox(height: 12),
          RouteMap(stops: stops, height: 280),
          const SizedBox(height: 8),
          OutlinedButton.icon(
            onPressed: _navigateToNextStop,
            icon: const Icon(Icons.navigation_outlined),
            label: const Text('Navigate to next stop'),
          ),
          if (_error != null) ...[
            const SizedBox(height: 10),
            NoticeStrip(message: _error!, isError: true),
          ],
          if (_sharingLocation)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 8),
              child: StateLabel('Sharing live location'),
            ),
          const SizedBox(height: 14),
          Text('Stops', style: Theme.of(context).textTheme.titleMedium),
          for (final stop in stops)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: CircleAvatar(
                radius: 15,
                backgroundColor: stop.completed
                    ? RouteAtlasColors.secondaryMuted
                    : RouteAtlasColors.tealSurface,
                child: Text(
                  '${stop.sequence}',
                  style: const TextStyle(color: RouteAtlasColors.ink),
                ),
              ),
              title: Text(stop.name),
              subtitle: Text(
                stop.completed
                    ? 'Passed'
                    : '${stop.latitude.toStringAsFixed(5)}, ${stop.longitude.toStringAsFixed(5)}',
              ),
              trailing: stop.completed
                  ? const Icon(Icons.check_circle_outline)
                  : stop.id == nextStop?.id
                  ? TextButton(
                      onPressed: () => _arrive(stop),
                      child: const Text('Arrived'),
                    )
                  : const Text('Upcoming'),
            ),
          const Divider(height: 30),
          Row(
            children: [
              Expanded(
                child: Text(
                  'Booking requests',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              Text('${_bookings.length}'),
            ],
          ),
          if (_bookings.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 20),
              child: Text('No bookings on this ride yet.'),
            ),
          for (final booking in _bookings)
            _BookingOperationRow(
              booking: booking,
              onAction: _bookingAction,
              onChat: () => context.push(
                '/chat/${booking['id']}?receiverId=${booking['passengerId']}&title=${Uri.encodeQueryComponent((booking['passengerName'] ?? 'Passenger').toString())}',
              ),
            ),
          const SizedBox(height: 16),
          TextButton.icon(
            onPressed: _cancelRide,
            icon: const Icon(Icons.cancel_outlined),
            label: const Text('Cancel ride'),
            style: TextButton.styleFrom(
              foregroundColor: RouteAtlasColors.coral,
            ),
          ),
        ],
      ),
    );
  }
}

class _BookingOperationRow extends StatelessWidget {
  const _BookingOperationRow({
    required this.booking,
    required this.onAction,
    required this.onChat,
  });

  final Map<String, dynamic> booking;
  final Future<void> Function(String id, String action) onAction;
  final VoidCallback onChat;

  @override
  Widget build(BuildContext context) {
    final id = (booking['id'] ?? '').toString();
    final status = (booking['status'] ?? 'Unknown').toString();
    final normalized = status.toLowerCase();
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  '${booking['passengerName'] ?? 'Passenger'}',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
              ),
              StateLabel(status),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            '${booking['pickupStopName'] ?? 'Pickup'}  →  ${booking['dropoffStopName'] ?? 'Drop-off'}',
          ),
          Text(
            'Requested ${booking['requestedAt'] ?? ''}',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 6),
          Wrap(
            spacing: 6,
            runSpacing: 4,
            children: [
              if (normalized == 'pending') ...[
                FilledButton.tonal(
                  onPressed: () => onAction(id, 'accept'),
                  child: const Text('Accept'),
                ),
                OutlinedButton(
                  onPressed: () => onAction(id, 'reject'),
                  child: const Text('Reject'),
                ),
              ],
              if (normalized == 'accepted' || normalized == 'active') ...[
                OutlinedButton(
                  onPressed: () => onAction(id, 'iam-here'),
                  child: const Text('I am here'),
                ),
                if (normalized == 'active')
                  OutlinedButton(
                    onPressed: () => onAction(id, 'end-ride'),
                    child: const Text('End ride'),
                  ),
                if (normalized == 'accepted')
                  OutlinedButton(
                    onPressed: () => onAction(id, 'no-show'),
                    child: const Text('Passenger no-show'),
                  ),
                if (normalized == 'active')
                  IconButton(
                    tooltip: 'Chat with passenger',
                    onPressed: onChat,
                    icon: const Icon(Icons.chat_bubble_outline),
                  ),
              ],
              if (normalized == 'completed' && booking['isCashPaid'] != true)
                FilledButton.tonal(
                  onPressed: () => onAction(id, 'cash-received'),
                  child: const Text('Cash received'),
                ),
            ],
          ),
          const Divider(height: 22),
        ],
      ),
    );
  }
}

class DriverLivenessScreen extends ConsumerStatefulWidget {
  const DriverLivenessScreen({super.key});

  @override
  ConsumerState<DriverLivenessScreen> createState() =>
      _DriverLivenessScreenState();
}

class _DriverLivenessScreenState extends ConsumerState<DriverLivenessScreen> {
  CameraController? _camera;
  String? _message;
  bool _loading = true;
  int _remaining = 10;
  Timer? _challengeTimer;
  static const _challenges = [
    'Blink twice',
    'Turn your head left',
    'Turn your head right',
    'Smile briefly',
  ];
  String _challenge = '';

  @override
  void initState() {
    super.initState();
    _restartChallenge();
    _initializeCamera();
  }

  void _restartChallenge() {
    _challengeTimer?.cancel();
    setState(() {
      _challenge = _challenges[Random.secure().nextInt(_challenges.length)];
      _remaining = 10;
      _message = null;
    });
    _challengeTimer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (!mounted) return;
      if (_remaining <= 1) {
        timer.cancel();
        setState(() {
          _remaining = 0;
          _message =
              'Challenge time expired. Report the failed attempt or try again.';
        });
      } else {
        setState(() => _remaining--);
      }
    });
  }

  Future<void> _reportFailure() async {
    try {
      final result = await ref
          .read(mobileApiProvider)
          .postData('driver/liveness/report-failure');
      if (!mounted) return;
      final attempts = result['attemptsRemaining'];
      final bannedUntil = result['bannedUntil'];
      setState(
        () => _message = bannedUntil != null
            ? 'Retry is blocked until $bannedUntil. Contact Admin if this is an error.'
            : 'Attempt recorded. ${attempts ?? ''} ${attempts == 1 ? 'attempt' : 'attempts'} remain.',
      );
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _message = failure.message);
    }
  }

  Future<void> _initializeCamera() async {
    try {
      final cameras = await availableCameras();
      final camera = cameras.firstWhere(
        (item) => item.lensDirection == CameraLensDirection.front,
        orElse: () => cameras.first,
      );
      final controller = CameraController(
        camera,
        ResolutionPreset.medium,
        enableAudio: false,
      );
      await controller.initialize();
      if (!mounted) return;
      setState(() {
        _camera = controller;
        _loading = false;
      });
    } catch (_) {
      if (mounted) {
        setState(() {
          _message = 'Camera access is unavailable. Check app permissions.';
          _loading = false;
        });
      }
    }
  }

  @override
  void dispose() {
    _challengeTimer?.cancel();
    _camera?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Driver liveness')),
    body: ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Text(
          'Follow the challenge',
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 8),
        const Text(
          'Your selfie is used for driver verification and is not sent until the verification model is configured.',
        ),
        const SizedBox(height: 18),
        if (_loading)
          const SizedBox(
            height: 280,
            child: LoadingPane(label: 'Opening camera'),
          )
        else if (_camera?.value.isInitialized == true)
          ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: AspectRatio(
              aspectRatio: _camera!.value.aspectRatio,
              child: CameraPreview(_camera!),
            ),
          )
        else
          const NoticeStrip(
            message: 'Camera preview is unavailable on this device.',
            isError: true,
          ),
        const SizedBox(height: 16),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: RouteAtlasColors.tealSurface,
            borderRadius: BorderRadius.circular(8),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(_challenge, style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 6),
              Text('$_remaining seconds remaining'),
            ],
          ),
        ),
        if (_message != null) ...[
          const SizedBox(height: 12),
          NoticeStrip(message: _message!, isError: true),
        ],
        const SizedBox(height: 18),
        const NoticeStrip(
          message: 'The face-api.js challenge model is not bundled in this project. Verification is intentionally unavailable rather than marking a selfie as a successful liveness check.',
          isError: true,
        ),
        const SizedBox(height: 14),
        FilledButton(onPressed: null, child: const Text('Verify identity')),
        OutlinedButton(
          onPressed: _reportFailure,
          child: const Text('I could not complete the challenge'),
        ),
        TextButton(
          onPressed: _restartChallenge,
          child: const Text('Try another challenge'),
        ),
        TextButton(
          onPressed: () async {
            await ref.read(apiClientProvider).clearSession();
            if (!context.mounted) return;
            ref.read(appSessionProvider.notifier).clear();
            context.go('/login');
          },
          child: const Text('Sign out'),
        ),
      ],
    ),
  );
}

class _ReadinessLine extends StatelessWidget {
  const _ReadinessLine({
    required this.icon,
    required this.title,
    required this.detail,
    required this.state,
    this.stateColor = RouteAtlasColors.teal,
  });

  final IconData icon;
  final String title;
  final String detail;
  final String state;
  final Color stateColor;

  @override
  Widget build(BuildContext context) => Row(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Icon(icon, color: stateColor),
      const SizedBox(width: 12),
      Expanded(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: const TextStyle(fontWeight: FontWeight.w700)),
            const SizedBox(height: 3),
            Text(detail, style: Theme.of(context).textTheme.bodySmall),
          ],
        ),
      ),
      const SizedBox(width: 8),
      StateLabel(state, color: stateColor),
    ],
  );
}

class _EditableStop {
  _EditableStop({required this.name, this.latitude, this.longitude});

  final TextEditingController name;
  double? latitude;
  double? longitude;
}

List<RouteStopPoint> _stops(Map<String, dynamic> ride) {
  final value = ride['stops'];
  if (value is! List) return const [];
  return value.map((item) => RouteStopPoint.fromJson(asMap(item))).toList()
    ..sort((a, b) => a.sequence.compareTo(b.sequence));
}

Map<String, dynamic>? _approvedCar(List<Map<String, dynamic>> cars) {
  for (final car in cars) {
    if ('${car['status']}'.toLowerCase() == 'approved') return car;
  }
  return null;
}
