import 'dart:convert';
import 'dart:io';

import 'package:flutter/cupertino.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:go_router/go_router.dart';

import 'core/network/api_client.dart';
import 'core/theme/route_atlas_theme.dart';
import 'features/chat/presentation/chat_screen.dart';
import 'features/driver/presentation/driver_screens.dart';
import 'features/auth/presentation/auth_screens.dart';
import 'features/auth/models/app_session.dart';
import 'features/auth/models/auth_session.dart';
import 'features/passenger/presentation/passenger_screens.dart';
import 'features/shared/role_home_screen.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  const storage = FlutterSecureStorage();
  final metadata = await storage.read(key: sessionMetadataStorageKey);
  if (metadata != null) {
    try {
      AppSessionController.restoredSession = AuthSession.fromJson(
        (jsonDecode(metadata) as Map).map(
          (key, value) => MapEntry('$key', value),
        ),
      );
    } catch (_) {
      await storage.delete(key: sessionMetadataStorageKey);
      await storage.delete(key: accessTokenStorageKey);
    }
  }
  runApp(const ProviderScope(child: PakiCarpoolApp()));
}

final appRouterProvider = Provider<GoRouter>((ref) {
  final router = GoRouter(
    initialLocation: ref.read(appSessionProvider) == null
        ? '/welcome'
        : '/home',
    routes: [
      GoRoute(
        path: '/welcome',
        builder: (context, state) => const WelcomeScreen(),
      ),
      GoRoute(
        path: '/login',
        builder: (context, state) => LoginScreen(
          verifiedEmail: state.uri.queryParameters['verified'] == '1',
        ),
      ),
      GoRoute(
        path: '/register',
        builder: (context, state) => RegistrationScreen(
          role: state.uri.queryParameters['role'] ?? 'Passenger',
        ),
      ),
      GoRoute(
        path: '/verify-email',
        builder: (context, state) =>
            EmailOtpScreen(email: state.uri.queryParameters['email'] ?? ''),
      ),
      GoRoute(
        path: '/forgot-password',
        builder: (context, state) => const ForgotPasswordScreen(),
      ),
      GoRoute(
        path: '/reset-password',
        builder: (context, state) => ResetPasswordScreen(
          email: state.uri.queryParameters['email'] ?? '',
          token: state.uri.queryParameters['token'] ?? '',
        ),
      ),
      GoRoute(
        path: '/google-auth',
        builder: (context, state) =>
            GoogleOAuthScreen(authorizationUrl: state.extra! as String),
      ),
      GoRoute(
        path: '/home',
        builder: (context, state) => const RoleHomeScreen(),
      ),
      GoRoute(
        path: '/passenger/ride/:rideId',
        builder: (context, state) =>
            PassengerRideDetailScreen(rideId: state.pathParameters['rideId']!),
      ),
      GoRoute(
        path: '/passenger/active',
        builder: (context, state) => const ActiveBookingScreen(),
      ),
      GoRoute(
        path: '/driver/car',
        builder: (context, state) => const CarRegistrationScreen(),
      ),
      GoRoute(
        path: '/driver/liveness',
        builder: (context, state) => const DriverLivenessScreen(),
      ),
      GoRoute(
        path: '/driver/ride/new',
        builder: (context, state) => const PostRideScreen(),
      ),
      GoRoute(
        path: '/driver/ride/active',
        builder: (context, state) => const ActiveRideScreen(),
      ),
      GoRoute(
        path: '/chat/:bookingId',
        builder: (context, state) => BookingChatScreen(
          bookingId: state.pathParameters['bookingId']!,
          receiverId: state.uri.queryParameters['receiverId'] ?? '',
          title: state.uri.queryParameters['title'] ?? 'Ride chat',
        ),
      ),
      GoRoute(
        path: '/feedback',
        builder: (context, state) => TripFeedbackScreen(
          rideId: state.uri.queryParameters['rideId'] ?? '',
          rateeId: state.uri.queryParameters['rateeId'] ?? '',
          role: state.uri.queryParameters['role'] ?? 'Passenger',
        ),
      ),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});

class PakiCarpoolApp extends ConsumerWidget {
  const PakiCarpoolApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(appRouterProvider);
    if (Platform.isIOS) {
      return CupertinoApp.router(
        title: 'PakiCarpool',
        theme: const CupertinoThemeData(
          primaryColor: RouteAtlasColors.teal,
          scaffoldBackgroundColor: RouteAtlasColors.canvas,
        ),
        routerConfig: router,
        debugShowCheckedModeBanner: false,
      );
    }

    return MaterialApp.router(
      title: 'PakiCarpool',
      theme: RouteAtlasTheme.light,
      darkTheme: RouteAtlasTheme.dark,
      themeMode: ThemeMode.system,
      routerConfig: router,
      debugShowCheckedModeBanner: false,
    );
  }
}
