import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/mobile_api.dart';
import '../../../core/realtime/signalr_service.dart';
import '../../../core/theme/route_atlas_theme.dart';
import '../../auth/models/app_session.dart';
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
  static const _radiusStorageKey = 'rideSearchRadius';

  final List<Map<String, dynamic>> _rides = [];
  final ScrollController _scrollController = ScrollController();
  final _startController = TextEditingController();
  final _endController = TextEditingController();
  Map<String, dynamic>? _activeBooking;
  Map<String, dynamic>? _selectedStart;
  Map<String, dynamic>? _selectedEnd;
  Map<String, dynamic>? _searchConfig;
  List<Map<String, dynamic>> _startSuggestions = [];
  List<Map<String, dynamic>> _endSuggestions = [];
  List<Map<String, dynamic>> _searchResults = [];
  Timer? _startDebounce;
  Timer? _endDebounce;
  double? _radius;
  int _page = 1;
  int _totalPages = 1;
  bool _loading = true;
  bool _loadingMore = false;
  bool _loadingStartSuggestions = false;
  bool _loadingEndSuggestions = false;
  bool _startNoSuggestions = false;
  bool _endNoSuggestions = false;
  bool _usingCurrentLocation = false;
  bool _searching = false;
  bool _hasSearched = false;
  String? _error;
  String? _searchError;
  String? _placesError;

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onRidesScroll);
    _load();
    _loadSearchConfig();
  }

  @override
  void dispose() {
    _startDebounce?.cancel();
    _endDebounce?.cancel();
    _scrollController.dispose();
    _startController.dispose();
    _endController.dispose();
    super.dispose();
  }

  Future<void> _loadSearchConfig() async {
    try {
      final config = await ref
          .read(mobileApiProvider)
          .getData('rides/search/config');
      final minimum = asDouble(config['minRadiusMeters']);
      final maximum = asDouble(config['maxRadiusMeters']);
      final defaultRadius = asDouble(config['defaultRadiusMeters']);
      if (minimum <= 0 ||
          maximum < minimum ||
          defaultRadius < minimum ||
          defaultRadius > maximum) {
        throw StateError('Ride search configuration is invalid.');
      }

      final storedRadius = await ref
          .read(secureStorageProvider)
          .read(key: _radiusStorageKey);
      final parsedRadius = double.tryParse(storedRadius ?? '');
      final radius = (parsedRadius ?? defaultRadius)
          .clamp(minimum, maximum)
          .toDouble();
      if (!mounted) return;
      setState(() {
        _searchConfig = config;
        _radius = radius;
      });
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _searchError = failure.message);
    } catch (_) {
      if (mounted) {
        setState(() => _searchError = 'Search settings could not be loaded.');
      }
    }
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

  void _onRidesScroll() {
    if (!_scrollController.hasClients ||
        _loading ||
        _loadingMore ||
        _page >= _totalPages) {
      return;
    }
    if (_scrollController.position.extentAfter <= 200) {
      unawaited(_load(append: true));
    }
  }

  Future<Map<String, dynamic>?> _loadActive(MobileApi api) async {
    try {
      return await api.getData('passenger/bookings/active');
    } on ApiFailure {
      return null;
    }
  }

  Future<void> _refreshAll() async {
    await Future.wait([_load(), _loadSearchConfig()]);
  }

  void _onStartChanged(String value) {
    _startDebounce?.cancel();
    setState(() {
      _selectedStart = null;
      _startSuggestions = [];
      _loadingStartSuggestions = false;
      _startNoSuggestions = false;
      _placesError = null;
    });
    _scheduleAutocomplete(value, isStart: true);
  }

  void _onEndChanged(String value) {
    _endDebounce?.cancel();
    setState(() {
      _selectedEnd = null;
      _endSuggestions = [];
      _loadingEndSuggestions = false;
      _endNoSuggestions = false;
      _placesError = null;
    });
    _scheduleAutocomplete(value, isStart: false);
  }

  void _scheduleAutocomplete(String value, {required bool isStart}) {
    final query = value.trim();
    if (query.isEmpty) return;
    final timer = Timer(
      const Duration(milliseconds: 300),
      () => _loadSuggestions(query, isStart: isStart),
    );
    if (isStart) {
      _startDebounce = timer;
    } else {
      _endDebounce = timer;
    }
  }

  Future<void> _loadSuggestions(String query, {required bool isStart}) async {
    if (!mounted) return;
    setState(() {
      if (isStart) {
        _loadingStartSuggestions = true;
      } else {
        _loadingEndSuggestions = true;
      }
      _placesError = null;
    });

    List<Map<String, dynamic>> suggestions = [];
    String? error;
    try {
      suggestions = await ref
          .read(mobileApiProvider)
          .getListData(
            'places/autocomplete',
            query: {
              'q': query,
              'lang': Localizations.localeOf(context).languageCode,
            },
          );
    } on ApiFailure catch (failure) {
      error = failure.statusCode == 503
          ? 'Place search is unavailable. Start Photon on the dev PC, use your current location for pickup, or retry this search later.'
          : failure.message;
    } catch (_) {
      error = 'Place suggestions could not be loaded. Try again.';
    }

    if (!mounted) return;
    final controller = isStart ? _startController : _endController;
    if (controller.text.trim() != query) return;
    setState(() {
      if (isStart) {
        _loadingStartSuggestions = false;
        _startSuggestions = suggestions;
        _startNoSuggestions = error == null && suggestions.isEmpty;
      } else {
        _loadingEndSuggestions = false;
        _endSuggestions = suggestions;
        _endNoSuggestions = error == null && suggestions.isEmpty;
      }
      _placesError = error;
    });
  }

  Future<void> _useCurrentLocation() async {
    if (_usingCurrentLocation) return;
    _startDebounce?.cancel();
    setState(() {
      _usingCurrentLocation = true;
      _loadingStartSuggestions = false;
      _startSuggestions = [];
      _startNoSuggestions = false;
      _placesError = null;
    });

    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        if (mounted) {
          setState(
            () => _placesError = 'Turn on location services to continue.',
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
            () => _placesError =
                'Allow location access to use your current location.',
          );
        }
        return;
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
        ),
      );
      final place = await ref
          .read(mobileApiProvider)
          .getData(
            'places/reverse',
            query: {'lat': position.latitude, 'lon': position.longitude},
          );
      final latitude = place['latitude'];
      final longitude = place['longitude'];
      final displayName = (place['displayName'] ?? place['name'] ?? '')
          .toString()
          .trim();
      if (latitude is! num || longitude is! num || displayName.isEmpty) {
        throw StateError('No address was found for your location.');
      }

      final selectedPlace = {
        ...place,
        'displayName': displayName,
        'latitude': latitude.toDouble(),
        'longitude': longitude.toDouble(),
      };
      if (!mounted) return;
      _startController.value = TextEditingValue(
        text: displayName,
        selection: TextSelection.collapsed(offset: displayName.length),
      );
      setState(() {
        _selectedStart = selectedPlace;
        _startSuggestions = [];
        _startNoSuggestions = false;
        _placesError = null;
      });
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _placesError = failure.message);
    } catch (_) {
      if (mounted) {
        setState(
          () => _placesError =
              'Your location could not be resolved. Enter an address instead.',
        );
      }
    } finally {
      if (mounted) setState(() => _usingCurrentLocation = false);
    }
  }

  void _selectPlace(Map<String, dynamic> place, {required bool isStart}) {
    final controller = isStart ? _startController : _endController;
    final displayName = (place['displayName'] ?? place['name'] ?? '')
        .toString();
    controller.value = TextEditingValue(
      text: displayName,
      selection: TextSelection.collapsed(offset: displayName.length),
    );
    setState(() {
      if (isStart) {
        _selectedStart = place;
        _startSuggestions = [];
      } else {
        _selectedEnd = place;
        _endSuggestions = [];
      }
      _placesError = null;
    });
  }

  void _changeRadius(double value) {
    final radius = value.roundToDouble();
    setState(() => _radius = radius);
    unawaited(_saveRadius(radius));
  }

  Future<void> _saveRadius(double radius) async {
    try {
      await ref
          .read(secureStorageProvider)
          .write(key: _radiusStorageKey, value: radius.toStringAsFixed(0));
    } catch (_) {
      if (mounted) {
        setState(() => _searchError = 'Search radius could not be saved.');
      }
    }
  }

  Future<void> _searchByRoute() async {
    final start = _selectedStart;
    final end = _selectedEnd;
    final radius = _radius;
    if (start == null || end == null || radius == null) {
      setState(
        () => _searchError = 'Choose both addresses from the suggestions.',
      );
      return;
    }

    setState(() {
      _searching = true;
      _hasSearched = true;
      _searchError = null;
    });
    try {
      final results = await ref
          .read(mobileApiProvider)
          .getListData(
            'rides/search',
            query: {
              'startLatitude': asDouble(start['latitude']),
              'startLongitude': asDouble(start['longitude']),
              'endLatitude': asDouble(end['latitude']),
              'endLongitude': asDouble(end['longitude']),
              'radiusMeters': radius,
            },
          );
      if (!mounted) return;
      setState(() => _searchResults = results);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _searchError = failure.message);
    } finally {
      if (mounted) setState(() => _searching = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const LoadingPane(label: 'Finding rides');
    if (_error != null) return ErrorPane(message: _error!, onRetry: _load);
    final searchConfig = _searchConfig;
    final radius = _radius;
    return RefreshIndicator(
      onRefresh: _refreshAll,
      child: ListView(
        controller: _scrollController,
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            'Find a shared route',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 5),
          const Text('Choose a ride that follows your part of the way.'),
          const SizedBox(height: 18),
          _PlaceAutocompleteInput(
            controller: _startController,
            label: 'Where are you getting picked up?',
            icon: Icons.trip_origin,
            suggestions: _startSuggestions,
            loading: _loadingStartSuggestions,
            noResults: _startNoSuggestions,
            onUseCurrentLocation: _useCurrentLocation,
            usingCurrentLocation: _usingCurrentLocation,
            onChanged: _onStartChanged,
            onSelected: (place) => _selectPlace(place, isStart: true),
          ),
          const SizedBox(height: 12),
          _PlaceAutocompleteInput(
            controller: _endController,
            label: 'Where are you going?',
            icon: Icons.place_outlined,
            suggestions: _endSuggestions,
            loading: _loadingEndSuggestions,
            noResults: _endNoSuggestions,
            onChanged: _onEndChanged,
            onSelected: (place) => _selectPlace(place, isStart: false),
          ),
          if (_placesError != null) ...[
            const SizedBox(height: 10),
            NoticeStrip(message: _placesError!, isError: true),
          ],
          if (searchConfig != null && radius != null) ...[
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: Text(
                    'Search radius',
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                Text('${radius.round()}m'),
              ],
            ),
            Slider(
              min: asDouble(searchConfig['minRadiusMeters']),
              max: asDouble(searchConfig['maxRadiusMeters']),
              value: radius,
              onChanged: _changeRadius,
            ),
          ] else ...[
            const SizedBox(height: 14),
            const LinearProgressIndicator(),
          ],
          const SizedBox(height: 8),
          FilledButton.icon(
            onPressed: _searching || searchConfig == null
                ? null
                : _searchByRoute,
            icon: _searching
                ? const SizedBox.square(
                    dimension: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.search),
            label: const Text('Search rides'),
          ),
          if (_searchError != null) ...[
            const SizedBox(height: 10),
            NoticeStrip(message: _searchError!, isError: true),
          ],
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
          if (_hasSearched) ...[
            Text(
              'Matching rides',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 6),
            if (_searching) const LinearProgressIndicator(),
            if (!_searching && _searchResults.isEmpty)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 28),
                child: Text('No rides match this route and radius.'),
              ),
            for (final result in _searchResults)
              _RideSearchResultCard(
                result: result,
                onTap: () =>
                    context.push('/passenger/ride/${result['rideId']}'),
              ),
          ] else if (_rides.isEmpty)
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
          if (_loadingMore)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 18),
              child: const Center(
                child: SizedBox.square(
                  dimension: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            ),
          const SizedBox(height: 12),
          if (!_hasSearched)
            Text(
              'Routes and stop order come from driver posts. Segment availability is confirmed when you request a seat.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
        ],
      ),
    );
  }
}

class _PlaceAutocompleteInput extends StatelessWidget {
  const _PlaceAutocompleteInput({
    required this.controller,
    required this.label,
    required this.icon,
    required this.suggestions,
    required this.loading,
    required this.noResults,
    this.onUseCurrentLocation,
    this.usingCurrentLocation = false,
    required this.onChanged,
    required this.onSelected,
  });

  final TextEditingController controller;
  final String label;
  final IconData icon;
  final List<Map<String, dynamic>> suggestions;
  final bool loading;
  final bool noResults;
  final VoidCallback? onUseCurrentLocation;
  final bool usingCurrentLocation;
  final ValueChanged<String> onChanged;
  final ValueChanged<Map<String, dynamic>> onSelected;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      TextField(
        controller: controller,
        enabled: !usingCurrentLocation,
        onChanged: onChanged,
        textInputAction: TextInputAction.search,
        decoration: InputDecoration(
          labelText: label,
          prefixIcon: Icon(icon),
          suffixIcon: onUseCurrentLocation == null
              ? null
              : IconButton(
                  tooltip: 'Use current location',
                  onPressed: usingCurrentLocation ? null : onUseCurrentLocation,
                  icon: usingCurrentLocation
                      ? const SizedBox.square(
                          dimension: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.my_location),
                ),
        ),
      ),
      if (loading) const LinearProgressIndicator(minHeight: 2),
      if (suggestions.isNotEmpty || noResults)
        Container(
          margin: const EdgeInsets.only(top: 4),
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.surface,
            border: Border.all(color: RouteAtlasColors.outline),
            borderRadius: BorderRadius.circular(8),
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (noResults)
                const ListTile(
                  enabled: false,
                  title: Text('No places found. Try another search.'),
                ),
              for (var index = 0; index < suggestions.length; index++) ...[
                ListTile(
                  leading: const Icon(
                    Icons.location_on_outlined,
                    color: RouteAtlasColors.teal,
                  ),
                  title: Text(
                    (suggestions[index]['displayName'] ??
                            suggestions[index]['name'] ??
                            '')
                        .toString(),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  onTap: () => onSelected(suggestions[index]),
                ),
                if (index < suggestions.length - 1)
                  const Divider(height: 1, indent: 56),
              ],
            ],
          ),
        ),
    ],
  );
}

class _RideSearchResultCard extends StatelessWidget {
  const _RideSearchResultCard({required this.result, required this.onTap});

  final Map<String, dynamic> result;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final start = asMap(result['startStop']);
    final end = asMap(result['endStop']);
    final photoUrl = (result['driverPhotoUrl'] ?? '').toString();
    final departure = DateTime.tryParse('${result['departurTime']}')?.toLocal();
    final departureLabel = departure == null
        ? 'Departure time unavailable'
        : '${MaterialLocalizations.of(context).formatMediumDate(departure)} · '
              '${MaterialLocalizations.of(context).formatTimeOfDay(TimeOfDay.fromDateTime(departure))}';
    final availableSeats = asInt(result['availableSeats']);

    return Card(
      margin: const EdgeInsets.only(bottom: 10),
      color: Theme.of(context).colorScheme.surface,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(8),
        side: const BorderSide(color: RouteAtlasColors.outline),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  SizedBox.square(
                    dimension: 48,
                    child: ClipOval(
                      child: photoUrl.isEmpty
                          ? const ColoredBox(
                              color: RouteAtlasColors.tealSurface,
                              child: Icon(Icons.person_outline),
                            )
                          : Image.network(
                              photoUrl,
                              fit: BoxFit.cover,
                              errorBuilder: (context, error, stackTrace) =>
                                  const ColoredBox(
                                    color: RouteAtlasColors.tealSurface,
                                    child: Icon(Icons.person_outline),
                                  ),
                            ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text(
                      (result['driverName'] ?? 'Driver').toString(),
                      style: Theme.of(context).textTheme.titleMedium,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                  Text(
                    '$availableSeats ${availableSeats == 1 ? 'seat' : 'seats'}',
                    style: Theme.of(context).textTheme.labelLarge?.copyWith(
                      color: RouteAtlasColors.teal,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              _StopMatchLine(label: 'Pickup', stop: start),
              const SizedBox(height: 5),
              _StopMatchLine(label: 'Drop-off', stop: end),
              const SizedBox(height: 10),
              Text(
                departureLabel,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: RouteAtlasColors.muted),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _StopMatchLine extends StatelessWidget {
  const _StopMatchLine({required this.label, required this.stop});

  final String label;
  final Map<String, dynamic> stop;

  @override
  Widget build(BuildContext context) {
    final perfect = stop['isPerfectMatch'] == true;
    final distance = stop['distanceMeters'];
    final distanceLabel = !perfect && distance is num
        ? '${distance.round()}m away'
        : null;

    return Row(
      children: [
        SizedBox(
          width: 66,
          child: Text(
            label,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: RouteAtlasColors.secondaryMuted),
          ),
        ),
        Expanded(
          child: Text(
            (stop['stopName'] ?? '').toString(),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.bodyMedium,
          ),
        ),
        if (distanceLabel != null) ...[
          const SizedBox(width: 8),
          Text(
            distanceLabel,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: RouteAtlasColors.muted),
          ),
        ],
      ],
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
                LifecycleStatusSummary(
                  status: (ride['status'] ?? 'Posted').toString(),
                  kind: LifecycleStatusKind.ride,
                ),
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
    final futureStops = stops.where((stop) => stop.upcoming).toList();
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
            'Stops already en route or completed are unavailable. Segment capacity is checked by the server when you request.',
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

bool isBookingCancellable(String status) {
  final normalizedStatus = status.toLowerCase();
  return normalizedStatus == 'pending' || normalizedStatus == 'accepted';
}

class ActiveBookingScreen extends ConsumerStatefulWidget {
  const ActiveBookingScreen({this.onBrowseRides, super.key});

  final VoidCallback? onBrowseRides;

  @override
  ConsumerState<ActiveBookingScreen> createState() =>
      _ActiveBookingScreenState();
}

class _ActiveBookingScreenState extends ConsumerState<ActiveBookingScreen> {
  Map<String, dynamic>? _booking;
  Map<String, dynamic>? _ride;
  LatLng? _driverLocation;
  String? _error;
  String? _realtimeError;
  String? _eventNotice;
  bool _noActiveBooking = false;
  Timer? _refreshTimer;
  StreamSubscription<Map<String, dynamic>>? _notificationSubscription;
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
    _notificationSubscription = realtime.notifications.listen((notification) {
      if (!mounted) return;
      final message = notificationDisplayMessage(notification);
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(message)));
    });
    realtime.on('ReceiveLocationUpdate', (args) {
      if (args == null || args.isEmpty) return;
      final payload = asMap(args.first);
      final lat = asDouble(payload['lat']);
      final lng = asDouble(payload['lng']);
      if (mounted) setState(() => _driverLocation = LatLng(lat, lng));
    });
    realtime.on('BookingAccepted', (args) {
      if (!mounted) return;
      final payload = args == null || args.isEmpty
          ? const {}
          : asMap(args.first);
      final bookingId =
          payload['bookingId']?.toString() ?? _booking?['id']?.toString();
      if (bookingId != null && bookingId == _booking?['id']?.toString()) {
        setState(() {
          _eventNotice = 'Your ride request was accepted by the driver.';
          _booking = {..._booking ?? const {}, 'status': 'Accepted'};
        });
      }
    });
    realtime.on('BookingRejected', (args) {
      if (!mounted) return;
      final payload = args == null || args.isEmpty
          ? const {}
          : asMap(args.first);
      final reason =
          payload['reason']?.toString() ?? 'The driver rejected your booking.';
      setState(() => _eventNotice = reason);
      context.go('/home');
    });
    realtime.on('BookingAutoCancelled', (args) {
      if (!mounted) return;
      final payload = args == null || args.isEmpty
          ? const {}
          : asMap(args.first);
      final reason =
          payload['reason']?.toString() ??
          'This ride was automatically cancelled.';
      setState(() => _eventNotice = reason);
      context.go('/home');
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
        _noActiveBooking = false;
        _realtimeError = null;
        if (lat != null && lng != null) {
          _driverLocation = LatLng(asDouble(lat), asDouble(lng));
        }
        _loading = false;
        _error = null;
      });
      final passengerId = ref.read(appSessionProvider)?.userId;
      final bookingStatus = booking['status']?.toString().toLowerCase();
      if (rideId != null &&
          passengerId != null &&
          passengerId.isNotEmpty &&
          (bookingStatus == 'accepted' || bookingStatus == 'active')) {
        try {
          final realtime = ref.read(signalRServiceProvider);
          await realtime.connect();
          await realtime.joinRide(rideId);
          await realtime.joinPassengerRide(rideId, passengerId);
        } catch (error) {
          if (mounted) {
            setState(() {
              _realtimeError =
                  'Live ride updates are unavailable. Refresh to retry. ($error)';
            });
          }
        }
      }
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _noActiveBooking = failure.statusCode == 404 ||
            failure.message.contains('No active booking');
        _error = _noActiveBooking ? null : failure.message;
        _loading = false;
        if (_noActiveBooking) {
          _booking = null;
        }
      });
      if (failure.statusCode != 404) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(failure.message),
            action: SnackBarAction(
              label: 'Retry',
              onPressed: () => unawaited(_load()),
            ),
          ),
        );
      }
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
          'Report only after the ride has started, at least 3 minutes have passed, and the driver has not arrived at your pickup stop.',
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
          .postData('passenger/bookings/$bookingId/no-show');
      if (mounted) setState(() => _eventNotice = 'No-show report submitted.');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    }
  }

  Future<void> _openFeedback() async {
    final rideId = _booking?['rideId']?.toString();
    // The rating API expects the driver's user ID, not the ride DTO's
    // driverId (which is the driver-profile ID).
    final rateeId = _ride?['driverUserId']?.toString();
    if (rideId == null || rateeId == null || !mounted) return;
    context.push('/feedback?rideId=$rideId&rateeId=$rateeId&role=Passenger');
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    final notificationSubscription = _notificationSubscription;
    if (notificationSubscription != null) {
      unawaited(notificationSubscription.cancel());
    }
    final rideId = _booking?['rideId']?.toString();
    final passengerId = ref.read(appSessionProvider)?.userId;
    if (rideId != null) {
      unawaited(ref.read(signalRServiceProvider).leaveRide(rideId));
      if (passengerId != null && passengerId.isNotEmpty) {
        unawaited(
          ref
              .read(signalRServiceProvider)
              .leavePassengerRide(rideId, passengerId),
        );
      }
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: LoadingPane(label: 'Loading your booking'));
    }
    if (_booking == null) {
      if (_noActiveBooking) {
        return Scaffold(
          appBar: AppBar(title: const Text('Active booking')),
          body: Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.event_seat_outlined, size: 42),
                  const SizedBox(height: 12),
                  Text(
                    'No active booking',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  const SizedBox(height: 6),
                  const Text(
                    'Your ride requests and current bookings will appear here.',
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 12),
                  FilledButton(
                    onPressed:
                        widget.onBrowseRides ?? () => context.go('/home'),
                    child: const Text('Browse rides'),
                  ),
                ],
              ),
            ),
          ),
        );
      }
      return Scaffold(
        appBar: AppBar(title: const Text('Active booking')),
        body: ErrorPane(message: _error ?? 'The booking could not be loaded.', onRetry: _load),
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
              LifecycleStatusSummary(
                status: status,
                kind: LifecycleStatusKind.booking,
              ),
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
          if (_realtimeError != null) ...[
            const SizedBox(height: 12),
            NoticeStrip(message: _realtimeError!, isError: true),
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
              status.toLowerCase() == 'active') ...[
            OutlinedButton.icon(
              onPressed: _noShow,
              icon: const Icon(Icons.report_gmailerrorred_outlined),
              label: const Text('Driver has not arrived'),
            ),
          ],
          if (isBookingCancellable(status))
            TextButton(onPressed: _cancel, child: const Text('Cancel booking')),
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
