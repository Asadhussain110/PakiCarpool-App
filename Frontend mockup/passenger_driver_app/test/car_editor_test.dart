import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pakicarpool_user_app/core/network/mobile_api.dart';
import 'package:pakicarpool_user_app/features/driver/presentation/driver_screens.dart';

void main() {
  testWidgets('car editor starts with the seat capacity returned by the API', (
    tester,
  ) async {
    final api = _FakeMobileApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [mobileApiProvider.overrideWithValue(api)],
        child: const MaterialApp(home: CarRegistrationScreen(carId: 'car-1')),
      ),
    );
    await tester.pumpAndSettle();

    expect(api.requestedPath, 'driver/car/status');
    expect(find.text('5'), findsOneWidget);
    expect(find.text('3'), findsNothing);
  });
}

class _FakeMobileApi extends Fake implements MobileApi {
  String? requestedPath;

  @override
  Future<List<Map<String, dynamic>>> getListData(
    String path, {
    Map<String, dynamic>? query,
  }) async {
    requestedPath = path;
    return const [
      {
        'id': 'car-1',
        'make': 'Test',
        'model': 'Car',
        'year': 2026,
        'availableSeats': 5,
        'status': 'Approved',
      },
    ];
  }
}
