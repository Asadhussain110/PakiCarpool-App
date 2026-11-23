import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pakicarpool_user_app/core/network/api_client.dart';

void main() {
  test('includes ASP.NET validation title and field messages', () {
    final request = RequestOptions(path: '/driver/car');
    final failure = ApiFailure.fromDio(
      DioException(
        requestOptions: request,
        response: Response<dynamic>(
          requestOptions: request,
          statusCode: 400,
          data: {
            'title': 'One or more validation errors occurred.',
            'errors': {
              'Year': ['The field Year must be between 2000 and 2030.'],
              'PhotoPaths': ['Upload each car photo and use the returned URL.'],
            },
          },
        ),
      ),
    );

    expect(failure.statusCode, 400);
    expect(failure.fieldErrors['Year'], [
      'The field Year must be between 2000 and 2030.',
    ]);
    expect(
      failure.message,
      contains('One or more validation errors occurred.'),
    );
    expect(failure.message, contains('Year: The field Year must be between'));
    expect(failure.message, contains('PhotoPaths: Upload each car photo'));
  });
}
