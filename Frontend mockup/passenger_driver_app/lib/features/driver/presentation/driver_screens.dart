import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:camera/camera.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:google_mlkit_face_detection/google_mlkit_face_detection.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';
import 'package:latlong2/latlong.dart';
import 'package:signalr_netcore/signalr_client.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/mobile_api.dart';
import '../../../core/realtime/signalr_service.dart';
import '../../../core/theme/route_atlas_theme.dart';
import '../../auth/models/app_session.dart';
import '../../auth/models/auth_session.dart';
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
  const CarRegistrationScreen({super.key, this.carId});

  final String? carId;

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
  bool _submitting = false;
  bool _seatsChanged = false;
  String? _error;

  bool get _isEditing => widget.carId != null;

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
      Map<String, dynamic>? editingCar;
      if (widget.carId != null) {
        for (final car in statuses) {
          if (car['id']?.toString() == widget.carId) {
            editingCar = car;
            break;
          }
        }
        if (editingCar == null) {
          setState(() {
            _statuses = statuses;
            _error = 'Car details could not be found.';
            _loading = false;
          });
          return;
        }
        _make.text = editingCar['make']?.toString() ?? '';
        _model.text = editingCar['model']?.toString() ?? '';
        _year.text = editingCar['year']?.toString() ?? '';
      }
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

  Future<String> _uploadCarDocument(
    XFile image, {
    required bool registration,
  }) async {
    final file = await MultipartFile.fromFile(image.path, filename: image.name);
    final result = await ref
        .read(mobileApiProvider)
        .postData(
          'driver/car/documents',
          body: FormData.fromMap({
            'file': file,
            'documentType': registration ? 'registration' : 'carPhoto',
          }),
        );
    final path = result['path']?.toString();
    if (path == null || path.isEmpty) {
      throw const ApiFailure(
        message: 'The server did not return the uploaded document path.',
      );
    }
    return path;
  }

  Future<void> _submitCar() async {
    if (!_formKey.currentState!.validate()) return;
    if (!_isEditing && _carPhotos.isEmpty) {
      setState(() => _error = 'Choose at least one exterior photo.');
      return;
    }
    if (!_isEditing && _registrationPhoto == null) {
      setState(() => _error = 'Choose the vehicle registration document.');
      return;
    }

    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final body = <String, dynamic>{
        'make': _make.text.trim(),
        'model': _model.text.trim(),
        'year': int.parse(_year.text.trim()),
      };
      if (_isEditing) {
        final licensePlate = _plate.text.trim();
        if (licensePlate.isNotEmpty) body['licensePlate'] = licensePlate;
        if (_seatsChanged) body['availableSeats'] = _seats;
        if (_carPhotos.isNotEmpty) {
          body['photoPaths'] = [
            for (final image in _carPhotos)
              await _uploadCarDocument(image, registration: false),
          ];
        }
        if (_registrationPhoto != null) {
          body['regDocPath'] = await _uploadCarDocument(
            _registrationPhoto!,
            registration: true,
          );
        }
        await ref
            .read(mobileApiProvider)
            .putData('driver/car/${widget.carId}', body: body);
      } else {
        body.addAll({
          'licensePlate': _plate.text.trim(),
          'availableSeats': _seats,
          'photoPaths': [
            for (final image in _carPhotos)
              await _uploadCarDocument(image, registration: false),
          ],
          'regDocPath': await _uploadCarDocument(
            _registrationPhoto!,
            registration: true,
          ),
        });
        await ref.read(mobileApiProvider).postData('driver/car', body: body);
      }
      if (!mounted) return;
      context.go('/driver/car/status');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    } catch (error) {
      if (mounted) {
        setState(() => _error = 'Could not submit the car details: $error');
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
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
    appBar: AppBar(
      title: Text(_isEditing ? 'Edit car' : 'Car approval'),
      actions: [
        IconButton(
          tooltip: 'Car status',
          onPressed: () => context.push('/driver/car/status'),
          icon: const Icon(Icons.fact_check_outlined),
        ),
      ],
    ),
    body: _loading
        ? const LoadingPane(label: 'Loading vehicle status')
        : ListView(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
            children: [
              Text(
                _isEditing ? 'Update your car' : 'Register your car',
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
                      required: !_isEditing,
                      maxLength: 20,
                    ),
                    const SizedBox(height: 8),
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            _isEditing
                                ? 'Available seats (optional update)'
                                : 'Available seats',
                          ),
                        ),
                        IconButton(
                          tooltip: 'Remove one seat',
                          onPressed: _seats > 1
                              ? () => setState(() {
                                  _seats--;
                                  _seatsChanged = true;
                                })
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
                              ? () => setState(() {
                                  _seats++;
                                  _seatsChanged = true;
                                })
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
                        _isEditing && _carPhotos.isEmpty
                            ? 'Keep current exterior photos'
                            : 'Choose exterior photos (${_carPhotos.length})',
                      ),
                    ),
                    if (_carPhotos.isNotEmpty)
                      Text('${_carPhotos.length} replacement photos selected.'),
                    OutlinedButton.icon(
                      onPressed: _pickRegistrationPhoto,
                      icon: const Icon(Icons.description_outlined),
                      label: Text(
                        _registrationPhoto == null
                            ? _isEditing
                                  ? 'Keep current registration document'
                                  : 'Choose registration document'
                            : 'Document selected',
                      ),
                    ),
                    const SizedBox(height: 12),
                    if (_error != null)
                      NoticeStrip(message: _error!, isError: true),
                    const SizedBox(height: 12),
                    FilledButton.icon(
                      onPressed: _submitting ? null : _submitCar,
                      icon: _submitting
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.upload_outlined),
                      label: Text(
                        _submitting
                            ? 'Submitting...'
                            : _isEditing
                            ? 'Save car changes'
                            : 'Submit for approval',
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
  );
}

class DriverCarStatusScreen extends ConsumerStatefulWidget {
  const DriverCarStatusScreen({super.key});

  @override
  ConsumerState<DriverCarStatusScreen> createState() =>
      _DriverCarStatusScreenState();
}

class _DriverCarStatusScreenState extends ConsumerState<DriverCarStatusScreen> {
  List<Map<String, dynamic>> _cars = const [];
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
    try {
      final cars = await ref
          .read(mobileApiProvider)
          .getListData('driver/car/status');
      if (!mounted) return;
      setState(() {
        _cars = cars;
        _loading = false;
      });
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() {
          _error = failure.message;
          _loading = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _error = 'Could not load car status. Try again.';
          _loading = false;
        });
      }
    }
  }

  Widget _carTile(BuildContext context, Map<String, dynamic> car) {
    final status = car['status']?.toString() ?? 'Unknown';
    final carId = car['id']?.toString();
    final isRejected = status.toLowerCase() == 'rejected';
    final isApproved = status.toLowerCase() == 'approved';
    final isPending = status.toLowerCase() == 'pending';
    final rejectionReason = car['rejectionReason']?.toString().trim();

    return Column(
      children: [
        ListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(
            '${car['make'] ?? ''} ${car['model'] ?? ''} · ${car['year'] ?? ''}',
          ),
          subtitle: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              StateLabel(
                status,
                color: isRejected
                    ? RouteAtlasColors.coral
                    : status.toLowerCase() == 'approved'
                    ? RouteAtlasColors.teal
                    : RouteAtlasColors.secondaryMuted,
              ),
              if (isRejected) ...[
                const SizedBox(height: 6),
                NoticeStrip(
                  message:
                      'Rejection reason: ${rejectionReason?.isNotEmpty == true ? rejectionReason : 'No reason provided.'}',
                  isError: true,
                ),
                TextButton.icon(
                  onPressed: carId == null
                      ? null
                      : () => context.push('/driver/car/$carId/edit'),
                  icon: const Icon(Icons.edit_outlined),
                  label: const Text('Edit and resubmit'),
                ),
              ],
            ],
          ),
          trailing: isApproved || isPending
              ? IconButton(
                  tooltip: isPending
                      ? 'Edit pending car details'
                      : 'Edit car details',
                  onPressed: carId == null
                      ? null
                      : () => context.push('/driver/car/$carId/edit'),
                  icon: const Icon(Icons.edit_outlined),
                )
              : null,
        ),
        const Divider(height: 1),
      ],
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Car status'),
      actions: [
        IconButton(
          tooltip: 'Refresh status',
          onPressed: _loading ? null : _load,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: _loading
        ? const LoadingPane(label: 'Loading car status')
        : _error != null
        ? ErrorPane(message: _error!, onRetry: _load)
        : ListView(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
            children: [
              if (_cars.isEmpty) ...[
                const Text('No cars registered yet.'),
                const SizedBox(height: 12),
              ],
              for (final car in _cars) _carTile(context, car),
              const SizedBox(height: 16),
              OutlinedButton.icon(
                onPressed: () => context.push('/driver/car'),
                icon: const Icon(Icons.add),
                label: const Text('Register a car'),
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

class DriverLivenessPendingScreen extends ConsumerStatefulWidget {
  const DriverLivenessPendingScreen({super.key});

  @override
  ConsumerState<DriverLivenessPendingScreen> createState() =>
      _DriverLivenessPendingScreenState();
}

class _DriverLivenessPendingScreenState
    extends ConsumerState<DriverLivenessPendingScreen> {
  Timer? _timer;
  bool _loading = true;
  bool _polling = false;
  String? _status;
  String? _error;

  @override
  void initState() {
    super.initState();
    _pollStatus();
    _timer = Timer.periodic(const Duration(seconds: 10), (_) => _pollStatus());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _pollStatus() async {
    if (_polling) return;
    _polling = true;
    try {
      final result = await ref
          .read(mobileApiProvider)
          .getBareData('driver/liveness/status');
      final status = result['status']?.toString();
      if (!mounted) return;
      setState(() {
        _loading = false;
        _status = status;
        _error = null;
      });
      if (status == 'Approved') {
        final accessToken = result['accessToken']?.toString();
        if (accessToken == null || accessToken.isEmpty) {
          throw const ApiFailure(
            message: 'The server approved liveness without returning an access token.',
          );
        }
        final apiClient = ref.read(apiClientProvider);
        await apiClient.saveAccessToken(accessToken);
        final session = ref.read(appSessionProvider);
        if (session != null) {
          final updatedSession = AuthSession(
            accessToken: accessToken,
            userId: session.userId,
            email: session.email,
            fullName: session.fullName,
            role: session.role,
            livenessPending: false,
          );
          ref.read(appSessionProvider.notifier).set(updatedSession);
          await apiClient.saveSessionMetadata(
            jsonEncode(updatedSession.toJson()),
          );
        }
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Liveness approved. Welcome back.')),
          );
          context.go('/home');
        }
      } else if (status == 'Rejected') {
        final rejectionReason = result['rejectionReason']?.toString();
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text(
                rejectionReason?.isNotEmpty == true
                    ? 'Liveness rejected: $rejectionReason'
                    : 'Liveness was rejected.',
              ),
            ),
          );
          context.go('/driver/liveness');
        }
      } else if (status == 'NotSubmitted') {
        if (mounted) context.go('/driver/liveness');
      }
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = failure.message;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = 'Could not check liveness status. Try again.';
        });
      }
    } finally {
      _polling = false;
    }
  }

  @override
  Widget build(BuildContext context) {
    final status = _status ?? 'PendingReview';
    final message = switch (status) {
      'Approved' => 'Your identity check has been approved.',
      'Rejected' => 'Your liveness check was rejected.',
      'NotSubmitted' =>
        'Your previous submission was not approved. Start a new live check.',
      _ => 'Your selfie is under review. We will re-check automatically.',
    };

    return Scaffold(
      appBar: AppBar(title: const Text('Liveness pending')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Icon(Icons.verified_user_outlined, size: 64),
                const SizedBox(height: 18),
                Text(
                  'Verification in review',
                  style: Theme.of(context).textTheme.headlineSmall,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 12),
                Text(message, textAlign: TextAlign.center),
                const SizedBox(height: 18),
                if (_loading)
                  const Center(
                    child: SizedBox(
                      width: 24,
                      height: 24,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  )
                else if (_error != null)
                  NoticeStrip(message: _error!, isError: true),
                const SizedBox(height: 18),
                FilledButton(
                  onPressed: () => context.go('/driver/liveness'),
                  child: const Text('Retry verification'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class ActiveRideScreen extends ConsumerStatefulWidget {
  const ActiveRideScreen({this.onReturnToDashboard, super.key});

  final VoidCallback? onReturnToDashboard;

  @override
  ConsumerState<ActiveRideScreen> createState() => _ActiveRideScreenState();
}

class _ActiveRideScreenState extends ConsumerState<ActiveRideScreen> {
  Map<String, dynamic>? _ride;
  List<Map<String, dynamic>> _bookings = const [];
  bool _loading = true;
  bool _noActiveRide = false;
  bool _sharingLocation = false;
  String? _error;
  Timer? _locationTimer;
  bool _locating = false;
  bool _realtimeListening = false;
  StreamSubscription<Map<String, dynamic>>? _notificationSubscription;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
      _noActiveRide = false;
    });
    try {
      final api = ref.read(mobileApiProvider);
      final ride = await api.getData('driver/rides/active');
      final bookings = await api.getData('driver/rides/${ride['id']}/bookings');
      final items = bookings['items'];
      if (!mounted) return;
      setState(() {
        _ride = ride;
        _bookings = items is List ? items.map(asMap).toList() : const [];
        _loading = false;
      });
      _listenForBookingRequests();
      final rideStatus = '${ride['status']}'.toLowerCase();
      if (rideStatus == 'posted' || rideStatus == 'active') {
        await _startLocationSharing();
      }
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _noActiveRide = failure.statusCode == 400 || failure.statusCode == 404;
        _ride = null;
        _bookings = const [];
        _error = _noActiveRide ? null : failure.message;
        _loading = false;
      });
    }
  }

  void _listenForBookingRequests() {
    if (_realtimeListening) return;
    _realtimeListening = true;
    final realtime = ref.read(signalRServiceProvider);
    _notificationSubscription = realtime.notifications.listen((notification) {
      if (!mounted) return;
      final message = notificationDisplayMessage(notification);
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(message)));
    });
    realtime.on('BookingRequestReceived', (_) {
      unawaited(_load());
    });
    realtime.on('RideFullNotification', (args) {
      if (!mounted) return;
      final payload = args == null || args.isEmpty
          ? const {}
          : asMap(args.first);
      final message =
          payload['message']?.toString() ?? 'This ride is now full.';
      setState(() => _error = message);
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    });
    realtime.on('NavigationLinkUpdated', (args) {
      if (!mounted) return;
      final payload = args == null || args.isEmpty
          ? const {}
          : asMap(args.first);
      final label = payload['label']?.toString() ?? 'Navigation';
      final link = payload['link']?.toString() ?? '';
      if (link.isNotEmpty) {
        setState(() => _error = '$label: $link');
      }
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
          final rideId = _ride!['id'].toString();
          if (realtime.state == HubConnectionState.Connected) {
            await realtime.sendLocation(
              rideId,
              position.latitude,
              position.longitude,
            );
          } else {
            await ref
                .read(mobileApiProvider)
                .postData(
                  'driver/location',
                  body: {
                    'rideId': rideId,
                    'lat': position.latitude,
                    'lng': position.longitude,
                  },
                );
          }
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
      await ref.read(mobileApiProvider).postData('driver/bookings/$id/$action');
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
    try {
      await ref
          .read(mobileApiProvider)
          .postData('driver/stops/${next.id}/begin-travel');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
      return;
    }
    await _load();
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
          .postData('driver/stops/${stop.id}/arrive');
      await _load();
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    }
  }

  @override
  void dispose() {
    _locationTimer?.cancel();
    final notificationSubscription = _notificationSubscription;
    if (notificationSubscription != null) {
      unawaited(notificationSubscription.cancel());
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const LoadingPane(label: 'Loading active ride');
    if (_ride == null) {
      if (_noActiveRide) {
        return Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.route_outlined, size: 42),
                const SizedBox(height: 12),
                Text(
                  'No active ride',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 6),
                const Text(
                  'Post a ride from Home to start sharing your route.',
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 12),
                OutlinedButton(
                  onPressed: _load,
                  child: const Text('Check again'),
                ),
                TextButton(
                  onPressed:
                      widget.onReturnToDashboard ?? () => context.go('/home'),
                  child: const Text('Return to driver home'),
                ),
              ],
            ),
          ),
        );
      }
      return ErrorPane(
        message: _error ?? 'The active ride could not be loaded.',
        onRetry: _load,
      );
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
              LifecycleStatusSummary(
                status: status,
                kind: LifecycleStatusKind.ride,
              ),
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
                  : stop.id == nextStop?.id && stop.active
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
              LifecycleStatusSummary(
                status: status,
                kind: LifecycleStatusKind.booking,
              ),
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
                  onPressed: () => onAction(id, 'driver-arrived'),
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

enum _LivenessPrompt { blink, turnLeft, turnRight, smile }

extension on _LivenessPrompt {
  String get label => switch (this) {
    _LivenessPrompt.blink => 'Blink both eyes',
    _LivenessPrompt.turnLeft => 'Turn your head to the left',
    _LivenessPrompt.turnRight => 'Turn your head to the right',
    _LivenessPrompt.smile => 'Smile',
  };

  String get apiValue => switch (this) {
    _LivenessPrompt.blink => 'Blink',
    _LivenessPrompt.turnLeft => 'TurnLeft',
    _LivenessPrompt.turnRight => 'TurnRight',
    _LivenessPrompt.smile => 'Smile',
  };
}

class _DriverLivenessScreenState extends ConsumerState<DriverLivenessScreen> {
  static const _maxAttempts = 3;
  static const _promptTimeout = Duration(seconds: 10);

  final FaceDetector _faceDetector = FaceDetector(
    options: FaceDetectorOptions(
      enableClassification: true,
      enableTracking: true,
      performanceMode: FaceDetectorMode.accurate,
      minFaceSize: 0.15,
    ),
  );
  CameraController? _camera;
  String? _message;
  String? _challengeNonce;
  DateTime? _challengeExpiresAt;
  String? _challengeError;
  List<_LivenessPrompt> _prompts = const [];
  final Map<_LivenessPrompt, List<Map<String, double>>> _promptEvidence = {};
  List<Map<String, double>> _currentPromptFrames = [];
  int _promptIndex = 0;
  int _attemptNumber = 1;
  int _qualifyingFrames = 0;
  bool _cameraLoading = true;
  bool _challengeLoading = true;
  bool _attemptActive = false;
  bool _streamStarting = false;
  bool _processingFrame = false;
  bool _eyesWereClosed = false;
  bool _submitting = false;
  Timer? _promptTimer;

  @override
  void initState() {
    super.initState();
    _fetchChallenge();
    _initializeCamera();
  }

  Future<void> _fetchChallenge({bool startAttempt = true}) async {
    setState(() {
      _challengeLoading = true;
      _challengeError = null;
      _challengeNonce = null;
      _challengeExpiresAt = null;
      _message = null;
    });
    try {
      final result = await ref
          .read(mobileApiProvider)
          .getBareData('driver/liveness/challenge');
      final nonce = result['nonce']?.toString();
      final expiresAt = DateTime.tryParse(
        result['expiresAt']?.toString() ?? '',
      );
      final prompts = _parseServerPrompts(result['prompts']);
      if (nonce == null ||
          nonce.isEmpty ||
          expiresAt == null ||
          prompts == null) {
        throw const ApiFailure(
          message: 'The server returned an invalid liveness challenge.',
        );
      }
      if (!mounted) return;
      setState(() {
        _challengeNonce = nonce;
        _challengeExpiresAt = expiresAt.toUtc();
        _prompts = prompts;
        _promptIndex = 0;
        _promptEvidence.clear();
        _currentPromptFrames = [];
        _challengeLoading = false;
      });
      if (startAttempt) await _maybeStartAttempt();
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() {
          _challengeError = failure.message;
          _challengeLoading = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _challengeError = 'Could not load a liveness challenge. Try again.';
          _challengeLoading = false;
        });
      }
    }
  }

  List<_LivenessPrompt>? _parseServerPrompts(Object? value) {
    if (value is! List) return null;
    final prompts = <_LivenessPrompt>[];
    for (final item in value) {
      final prompt = switch (item?.toString()) {
        'Blink' => _LivenessPrompt.blink,
        'TurnLeft' => _LivenessPrompt.turnLeft,
        'TurnRight' => _LivenessPrompt.turnRight,
        'Smile' => _LivenessPrompt.smile,
        _ => null,
      };
      if (prompt == null) return null;
      prompts.add(prompt);
    }
    if (prompts.length != 3 || prompts.toSet().length != 3) return null;
    return List.unmodifiable(prompts);
  }

  Future<void> _maybeStartAttempt() async {
    final camera = _camera;
    if (_attemptActive ||
        _streamStarting ||
        _challengeLoading ||
        _challengeNonce == null ||
        _attemptNumber > _maxAttempts ||
        camera == null ||
        !camera.value.isInitialized) {
      return;
    }
    if (_challengeExpiresAt == null ||
        !DateTime.now().toUtc().isBefore(_challengeExpiresAt!)) {
      await _fetchChallenge();
      return;
    }

    _streamStarting = true;
    _attemptActive = true;
    _startPromptTimer();
    try {
      await camera.startImageStream(_processCameraImage);
    } catch (error) {
      _attemptActive = false;
      _promptTimer?.cancel();
      if (mounted) {
        setState(() {
          _message = 'Could not start live face detection: $error';
          _cameraLoading = false;
        });
      }
    } finally {
      _streamStarting = false;
    }
  }

  void _startPromptTimer() {
    _promptTimer?.cancel();
    _qualifyingFrames = 0;
    _eyesWereClosed = false;
    _currentPromptFrames = [];
    _promptTimer = Timer(_promptTimeout, () {
      unawaited(
        _finishAttempt(passed: false, reason: 'That prompt timed out.'),
      );
    });
  }

  Future<void> _processCameraImage(CameraImage cameraImage) async {
    if (_processingFrame || !_attemptActive || _submitting) return;
    _processingFrame = true;
    try {
      final inputImage = _inputImageFromCameraImage(cameraImage);
      if (inputImage == null) {
        throw StateError('This camera image format is not supported.');
      }
      final faces = await _faceDetector.processImage(inputImage);
      if (!_attemptActive || !mounted) return;
      if (faces.length != 1) {
        _qualifyingFrames = 0;
        _eyesWereClosed = false;
        _currentPromptFrames = [];
        return;
      }
      final face = faces.single;
      final prompt = _prompts[_promptIndex];
      final leftEye = face.leftEyeOpenProbability;
      final rightEye = face.rightEyeOpenProbability;
      final headYaw = face.headEulerAngleY;
      final smile = face.smilingProbability;
      Map<String, double>? frame;
      var promptPassed = false;
      switch (prompt) {
        case _LivenessPrompt.blink:
          if (leftEye == null || rightEye == null) {
            _qualifyingFrames = 0;
            return;
          }
          frame = {
            'leftEyeOpenProbability': leftEye,
            'rightEyeOpenProbability': rightEye,
          };
          if (leftEye < 0.3 && rightEye < 0.3) _eyesWereClosed = true;
          promptPassed = _eyesWereClosed && leftEye > 0.7 && rightEye > 0.7;
        case _LivenessPrompt.turnLeft:
          if (headYaw == null) {
            _qualifyingFrames = 0;
            return;
          }
          frame = {'headEulerAngleY': headYaw};
          promptPassed = headYaw < -20;
        case _LivenessPrompt.turnRight:
          if (headYaw == null) {
            _qualifyingFrames = 0;
            return;
          }
          frame = {'headEulerAngleY': headYaw};
          promptPassed = headYaw > 20;
        case _LivenessPrompt.smile:
          if (smile == null) {
            _qualifyingFrames = 0;
            return;
          }
          frame = {'smilingProbability': smile};
          promptPassed = smile > 0.75;
      }
      _currentPromptFrames.add(frame);
      _qualifyingFrames = promptPassed ? _qualifyingFrames + 1 : 0;
      if (_qualifyingFrames >= 2) _completePrompt();
    } catch (error) {
      await _finishAttempt(
        passed: false,
        reason: 'Live face detection failed: $error',
      );
    } finally {
      _processingFrame = false;
    }
  }

  InputImage? _inputImageFromCameraImage(CameraImage image) {
    final camera = _camera;
    if (camera == null || image.planes.isEmpty) return null;

    final orientationDegrees = switch (camera.value.deviceOrientation) {
      DeviceOrientation.portraitUp => 0,
      DeviceOrientation.landscapeLeft => 90,
      DeviceOrientation.portraitDown => 180,
      DeviceOrientation.landscapeRight => 270,
    };
    final sensorOrientation = camera.description.sensorOrientation;
    final rotationDegrees =
        camera.description.lensDirection == CameraLensDirection.front
        ? (sensorOrientation + orientationDegrees) % 360
        : (sensorOrientation - orientationDegrees + 360) % 360;
    final rotation = InputImageRotationValue.fromRawValue(rotationDegrees);
    if (rotation == null) return null;

    late final Uint8List bytes;
    late final InputImageFormat format;
    late final int bytesPerRow;
    if (Platform.isAndroid) {
      if (image.planes.length != 3) return null;
      bytes = _yuv420ToNv21(image);
      format = InputImageFormat.nv21;
      bytesPerRow = image.width;
    } else if (Platform.isIOS &&
        image.format.group == ImageFormatGroup.bgra8888) {
      final plane = image.planes.first;
      bytes = plane.bytes;
      format = InputImageFormat.bgra8888;
      bytesPerRow = plane.bytesPerRow;
    } else {
      return null;
    }

    return InputImage.fromBytes(
      bytes: bytes,
      metadata: InputImageMetadata(
        size: Size(image.width.toDouble(), image.height.toDouble()),
        rotation: rotation,
        format: format,
        bytesPerRow: bytesPerRow,
      ),
    );
  }

  Uint8List _yuv420ToNv21(CameraImage image) {
    final yPlane = image.planes[0];
    final uPlane = image.planes[1];
    final vPlane = image.planes[2];
    final chromaWidth = (image.width + 1) ~/ 2;
    final chromaHeight = (image.height + 1) ~/ 2;
    final output = Uint8List(
      image.width * image.height + 2 * chromaWidth * chromaHeight,
    );
    var offset = 0;

    for (var row = 0; row < image.height; row++) {
      for (var column = 0; column < image.width; column++) {
        output[offset++] =
            yPlane.bytes[row * yPlane.bytesPerRow +
                column * (yPlane.bytesPerPixel ?? 1)];
      }
    }
    for (var row = 0; row < chromaHeight; row++) {
      for (var column = 0; column < chromaWidth; column++) {
        final uIndex =
            row * uPlane.bytesPerRow + column * (uPlane.bytesPerPixel ?? 1);
        final vIndex =
            row * vPlane.bytesPerRow + column * (vPlane.bytesPerPixel ?? 1);
        output[offset++] = vPlane.bytes[vIndex];
        output[offset++] = uPlane.bytes[uIndex];
      }
    }
    return output;
  }

  void _completePrompt() {
    _promptTimer?.cancel();
    _promptEvidence[_prompts[_promptIndex]] = List.unmodifiable(
      _currentPromptFrames,
    );
    _qualifyingFrames = 0;
    _eyesWereClosed = false;
    if (_promptIndex == _prompts.length - 1) {
      unawaited(_finishAttempt(passed: true));
      return;
    }
    setState(() => _promptIndex++);
    _startPromptTimer();
  }

  Future<void> _finishAttempt({required bool passed, String? reason}) async {
    if (!_attemptActive) return;
    if (passed &&
        (_challengeExpiresAt == null ||
            !DateTime.now().toUtc().isBefore(_challengeExpiresAt!))) {
      passed = false;
      reason = 'The challenge expired before submission.';
    }
    _attemptActive = false;
    _promptTimer?.cancel();
    await _stopImageStream();
    if (passed) {
      await _submitLiveness();
      return;
    }

    if (!mounted) return;
    final hasRetry = _attemptNumber < _maxAttempts;
    setState(() {
      _message = hasRetry
          ? '${reason ?? 'The live check did not pass'} Try again; ${_maxAttempts - _attemptNumber} retries remain.'
          : '${reason ?? 'The live check did not pass'} Request a fresh challenge to try again.';
      _attemptNumber++;
    });
    if (hasRetry) await _fetchChallenge();
  }

  Future<void> _stopImageStream() async {
    final camera = _camera;
    if (camera != null &&
        camera.value.isInitialized &&
        camera.value.isStreamingImages) {
      await camera.stopImageStream();
    }
  }

  Future<void> _retryAttempt() async {
    if (_submitting) return;
    if (_attemptNumber > _maxAttempts) _attemptNumber = 1;
    setState(() => _message = null);
    await _fetchChallenge();
    if (!mounted) return;
    if (_camera == null || !_camera!.value.isInitialized) {
      await _initializeCamera();
    }
  }

  Future<void> _submitLiveness() async {
    final nonce = _challengeNonce;
    final expiresAt = _challengeExpiresAt;
    if (nonce == null || expiresAt == null) return;
    if (!DateTime.now().toUtc().isBefore(expiresAt)) {
      await _finishAttempt(
        passed: false,
        reason: 'The challenge expired before submission.',
      );
      return;
    }

    setState(() {
      _submitting = true;
      _message = null;
    });
    try {
      await _stopImageStream();
      final camera = _camera;
      if (camera == null || !camera.value.isInitialized) {
        throw const ApiFailure(message: 'The live camera is unavailable.');
      }
      final selfie = await camera.takePicture();
      final selfieBase64 = base64Encode(await selfie.readAsBytes());
      final result = await ref
          .read(mobileApiProvider)
          .postBareData(
            'driver/liveness',
            body: {
              'nonce': nonce,
              'selfieBase64': selfieBase64,
              'promptResults': [
                for (final prompt in _prompts)
                  {
                    'prompt': prompt.apiValue,
                    'frames': _promptEvidence[prompt] ?? const [],
                  },
              ],
            },
          );
      if (!mounted) return;
      if (result['success'] == true && result['status'] == 'Approved') {
        final accessToken = result['accessToken']?.toString();
        if (accessToken == null || accessToken.isEmpty) {
          throw const ApiFailure(
            message: 'The server approved liveness without returning an access token.',
          );
        }
        final apiClient = ref.read(apiClientProvider);
        await apiClient.saveAccessToken(accessToken);
        final session = ref.read(appSessionProvider);
        if (session != null) {
          final updatedSession = AuthSession(
            accessToken: accessToken,
            userId: session.userId,
            email: session.email,
            fullName: session.fullName,
            role: session.role,
            livenessPending: false,
          );
          ref.read(appSessionProvider.notifier).set(updatedSession);
          await apiClient.saveSessionMetadata(
            jsonEncode(updatedSession.toJson()),
          );
        }
        if (mounted) context.go('/driver/car');
        return;
      }
      setState(() {
        _message =
            result['message']?.toString() ??
            'The server could not complete liveness verification.';
      });
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _message = failure.message);
    } catch (error) {
      if (mounted) {
        setState(
          () => _message = 'Could not submit liveness verification: $error',
        );
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  Future<void> _initializeCamera() async {
    try {
      final cameras = await availableCameras();
      final camera = cameras.firstWhere(
        (item) => item.lensDirection == CameraLensDirection.front,
        orElse: () => throw StateError('A front-facing camera is required.'),
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
        _cameraLoading = false;
      });
      await _maybeStartAttempt();
    } catch (_) {
      if (mounted) {
        setState(() {
          _message = 'The front camera is unavailable. Check app permissions and try again.';
          _cameraLoading = false;
        });
      }
    }
  }

  @override
  void dispose() {
    _promptTimer?.cancel();
    unawaited(_faceDetector.close());
    final camera = _camera;
    if (camera != null) unawaited(camera.dispose());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Driver liveness')),
    body: ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Text(
          'Verify your identity',
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 8),
        const Text(
          'Follow the server-selected live camera prompts. A selfie is captured only after the face checks pass.',
        ),
        const SizedBox(height: 18),
        if (_cameraLoading)
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
            message: 'Camera access is unavailable. Check app permissions.',
            isError: true,
          ),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: RouteAtlasColors.tealSurface,
            borderRadius: BorderRadius.circular(8),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                _challengeLoading
                    ? 'Loading challenge'
                    : _challengeNonce == null
                    ? 'Challenge unavailable'
                    : 'Attempt ${min(_attemptNumber, _maxAttempts)} of $_maxAttempts',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 6),
              if (_attemptActive && _prompts.isNotEmpty)
                Text(_prompts[_promptIndex].label),
              if (_challengeExpiresAt != null)
                Text('Expires ${_challengeExpiresAt!.toLocal().toString()}'),
            ],
          ),
        ),
        if (_challengeError != null) ...[
          const SizedBox(height: 12),
          NoticeStrip(message: _challengeError!, isError: true),
          const SizedBox(height: 8),
          OutlinedButton.icon(
            onPressed: _challengeLoading ? null : _retryAttempt,
            icon: const Icon(Icons.refresh),
            label: const Text('Retry live check'),
          ),
        ],
        if (_message != null) ...[
          const SizedBox(height: 12),
          NoticeStrip(message: _message!, isError: true),
        ],
        if (_attemptActive) ...[
          const SizedBox(height: 16),
          LinearProgressIndicator(value: (_promptIndex + 1) / _prompts.length),
          const SizedBox(height: 8),
          Text(
            'Keep your face centered and follow the prompt before the timer expires.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
        if (!_attemptActive &&
            !_challengeLoading &&
            _message != null &&
            _challengeError == null) ...[
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: _challengeLoading ? null : _retryAttempt,
            icon: const Icon(Icons.refresh),
            label: const Text('Retry live check'),
          ),
        ],
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
