import 'package:flutter_test/flutter_test.dart';
import 'package:pakicarpool_user_app/features/passenger/presentation/passenger_screens.dart';

void main() {
  test('Pending and Accepted bookings offer cancellation in the mobile app', () {
    expect(isBookingCancellable('Pending'), isTrue);
    expect(isBookingCancellable('pending'), isTrue);
    expect(isBookingCancellable('Accepted'), isTrue);
    expect(isBookingCancellable('accepted'), isTrue);
  });

  test('Active bookings do not offer cancellation rejected by the API', () {
    expect(isBookingCancellable('Active'), isFalse);
    expect(isBookingCancellable('active'), isFalse);
  });

  test('Other terminal booking states do not offer cancellation', () {
    expect(isBookingCancellable('Rejected'), isFalse);
    expect(isBookingCancellable('Completed'), isFalse);
    expect(isBookingCancellable('Cancelled'), isFalse);
  });
}
