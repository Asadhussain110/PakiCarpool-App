import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../core/theme/route_atlas_theme.dart';

class RouteStopPoint {
  const RouteStopPoint({
    required this.id,
    required this.name,
    required this.sequence,
    required this.latitude,
    required this.longitude,
    this.completed = false,
    this.etaSeconds,
  });

  final String id;
  final String name;
  final int sequence;
  final double latitude;
  final double longitude;
  final bool completed;
  final int? etaSeconds;

  LatLng get point => LatLng(latitude, longitude);

  factory RouteStopPoint.fromJson(Map<String, dynamic> json) => RouteStopPoint(
    id: json['id']?.toString() ?? '',
    name: json['name']?.toString() ?? 'Stop',
    sequence: _asInt(json['sequenceOrder']),
    latitude: _asDouble(json['lat'] ?? json['latitude']),
    longitude: _asDouble(json['lng'] ?? json['longitude']),
    completed: json['isCompleted'] == true,
    etaSeconds: json['etaSeconds'] == null ? null : _asInt(json['etaSeconds']),
  );
}

class RouteMap extends StatelessWidget {
  const RouteMap({
    required this.stops,
    this.driverLocation,
    this.pickupStopId,
    this.onMapTap,
    this.height = 280,
    super.key,
  });

  final List<RouteStopPoint> stops;
  final LatLng? driverLocation;
  final String? pickupStopId;
  final ValueChanged<LatLng>? onMapTap;
  final double height;

  @override
  Widget build(BuildContext context) {
    final validStops = stops.where(_validPoint).toList()
      ..sort((a, b) => a.sequence.compareTo(b.sequence));
    final points = validStops.map((stop) => stop.point).toList();
    if (driverLocation != null) points.add(driverLocation!);
    final center = points.isEmpty
        ? const LatLng(24.8607, 67.0011)
        : points.first;
    final markers = <Marker>[
      for (final stop in validStops)
        Marker(
          point: stop.point,
          width: 40,
          height: 44,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 28,
                height: 28,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: stop.id == pickupStopId
                      ? RouteAtlasColors.saffron
                      : stop.completed
                      ? RouteAtlasColors.secondaryMuted
                      : RouteAtlasColors.teal,
                  shape: BoxShape.circle,
                  border: Border.all(color: RouteAtlasColors.white, width: 2),
                ),
                child: Text(
                  '${stop.sequence}',
                  style: const TextStyle(
                    color: RouteAtlasColors.ink,
                    fontWeight: FontWeight.w700,
                    fontSize: 12,
                  ),
                ),
              ),
              Container(width: 2, height: 12, color: RouteAtlasColors.teal),
            ],
          ),
        ),
    ];
    final driverMarker = _driverMarker(driverLocation);
    if (driverMarker != null) markers.add(driverMarker);

    return SizedBox(
      height: height,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(12),
        child: FlutterMap(
          options: MapOptions(
            initialCenter: center,
            initialZoom: points.length > 1 ? 12 : 14,
            onTap: onMapTap == null ? null : (_, point) => onMapTap!(point),
          ),
          children: [
            TileLayer(
              urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
              userAgentPackageName: 'pk.pakicarpool.app',
            ),
            if (validStops.length > 1)
              PolylineLayer(
                polylines: [
                  Polyline(
                    points: validStops.map((stop) => stop.point).toList(),
                    strokeWidth: 4,
                    color: RouteAtlasColors.teal,
                  ),
                ],
              ),
            MarkerLayer(markers: markers),
            const RichAttributionWidget(
              attributions: [
                TextSourceAttribution('OpenStreetMap contributors'),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

Marker? _driverMarker(LatLng? point) => point == null
    ? null
    : Marker(
        point: point,
        width: 48,
        height: 48,
        child: Container(
          decoration: BoxDecoration(
            color: RouteAtlasColors.ink,
            shape: BoxShape.circle,
            border: Border.all(color: RouteAtlasColors.white, width: 3),
          ),
          child: const Icon(
            Icons.directions_car,
            color: RouteAtlasColors.white,
            size: 22,
          ),
        ),
      );

bool _validPoint(RouteStopPoint stop) =>
    stop.latitude.abs() <= 90 &&
    stop.longitude.abs() <= 180 &&
    (stop.latitude != 0 || stop.longitude != 0);

int _asInt(Object? value) => value is int ? value : int.tryParse('$value') ?? 0;

double _asDouble(Object? value) =>
    value is num ? value.toDouble() : double.tryParse('$value') ?? 0;
