// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:pakicarpool_user_app/features/auth/data/auth_repository.dart';
import 'package:pakicarpool_user_app/features/auth/models/app_session.dart';
import 'package:pakicarpool_user_app/features/auth/models/auth_session.dart';
import 'package:pakicarpool_user_app/features/auth/presentation/auth_screens.dart';

void main() {
  testWidgets('welcome screen lays out without exceptions', (tester) async {
    await tester.pumpWidget(const MaterialApp(home: WelcomeScreen()));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
  });

  testWidgets('login validates credentials before sending a request', (
    tester,
  ) async {
    final repository = _FakeAuthRepository();
    final container = ProviderContainer(
      overrides: [authRepositoryProvider.overrideWithValue(repository)],
    );
    addTearDown(container.dispose);
    final router = _testRouter();
    addTearDown(router.dispose);
    await tester.pumpWidget(_testApp(container, router));

    expect(find.text('Sign in'), findsOneWidget);
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();

    expect(find.text('Enter a valid email address.'), findsOneWidget);
    expect(find.text('Enter your password.'), findsOneWidget);
    expect(repository.signInCalls, 0);
  });

  testWidgets('login submits credentials and presents the authenticated name', (
    tester,
  ) async {
    final repository = _FakeAuthRepository();
    final container = ProviderContainer(
      overrides: [authRepositoryProvider.overrideWithValue(repository)],
    );
    addTearDown(container.dispose);
    final router = _testRouter();
    addTearDown(router.dispose);
    await tester.pumpWidget(_testApp(container, router));

    final fields = find.byType(TextFormField);
    await tester.enterText(fields.at(0), 'amina@example.com');
    await tester.enterText(fields.at(1), 'ValidPass!9');
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();

    expect(repository.signInCalls, 1);
    expect(find.text('Passenger home'), findsOneWidget);
    expect(container.read(appSessionProvider)?.fullName, 'Amina Khan');
  });

  testWidgets('email OTP screen enforces the resend cooldown', (tester) async {
    final repository = _FakeAuthRepository();
    final container = ProviderContainer(
      overrides: [authRepositoryProvider.overrideWithValue(repository)],
    );
    addTearDown(container.dispose);
    final router = GoRouter(
      initialLocation: '/verify-email?email=amina@example.com',
      routes: [
        GoRoute(
          path: '/login',
          builder: (context, state) => const Scaffold(body: Text('Login')),
        ),
        GoRoute(
          path: '/verify-email',
          builder: (context, state) =>
              EmailOtpScreen(email: state.uri.queryParameters['email'] ?? ''),
        ),
      ],
    );
    addTearDown(router.dispose);
    await tester.pumpWidget(_testApp(container, router));

    // The screen opens mid-cooldown: resending is not offered yet.
    expect(find.textContaining('request a new code in 60 seconds'), findsOneWidget);
    expect(find.text('Resend code'), findsNothing);

    await tester.pump(const Duration(seconds: 60));
    expect(find.text('Resend code'), findsOneWidget);

    await tester.tap(find.text('Resend code'));
    await tester.pumpAndSettle();

    expect(repository.resendCalls, 1);
    // A successful resend restarts the cooldown.
    expect(find.textContaining('request a new code in 60 seconds'), findsOneWidget);
    expect(find.text('Resend code'), findsNothing);
  });
}

GoRouter _testRouter() => GoRouter(
  initialLocation: '/login',
  routes: [
    GoRoute(path: '/login', builder: (context, state) => const LoginScreen()),
    GoRoute(
      path: '/home',
      builder: (context, state) => const Scaffold(body: Text('Passenger home')),
    ),
  ],
);

Widget _testApp(ProviderContainer container, GoRouter router) =>
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp.router(routerConfig: router),
    );

class _FakeAuthRepository implements AuthRepository {
  int signInCalls = 0;

  @override
  Future<AuthSession> signIn({
    required String email,
    required String password,
    String? hCaptchaToken,
  }) async {
    signInCalls++;
    return const AuthSession(
      accessToken: 'test-token',
      userId: 'test-user',
      email: 'amina@example.com',
      fullName: 'Amina Khan',
      role: PakiRole.passenger,
      livenessPending: false,
    );
  }

  @override
  Future<void> register({
    required String fullName,
    required String email,
    required String password,
    required PakiRole role,
  }) async {}

  @override
  Future<void> verifyEmail({
    required String email,
    required String otp,
  }) async {}

  @override
  Future<void> resendEmailVerification({required String email}) async {
    resendCalls++;
  }

  int resendCalls = 0;

  @override
  Future<void> forgotPassword({required String email}) async {}

  @override
  Future<void> resetPassword({
    required String email,
    required String code,
    required String newPassword,
  }) async {}

  @override
  Future<String> googleAuthorizationUrl({required PakiRole role}) async => '';

  @override
  Future<AuthSession> completeGoogleAuthorization({
    required String code,
    required String state,
  }) async => const AuthSession(
    accessToken: 'test-token',
    userId: 'test-user',
    email: 'amina@example.com',
    fullName: 'Amina Khan',
    role: PakiRole.passenger,
    livenessPending: false,
  );
}
