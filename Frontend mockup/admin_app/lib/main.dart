import 'dart:io';

import 'package:flutter/cupertino.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'admin_screens.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const ProviderScope(child: PakiCarpoolAdminApp()));
}

final adminPartialTokenProvider =
    NotifierProvider<AdminPartialTokenController, String?>(
      AdminPartialTokenController.new,
    );

class AdminPartialTokenController extends Notifier<String?> {
  @override
  String? build() => null;

  void set(String? token) => state = token;
}

final adminRouterProvider = Provider<GoRouter>((ref) {
  final router = GoRouter(
    initialLocation: '/login',
    routes: [
      GoRoute(
        path: '/login',
        builder: (context, state) => const AdminLoginScreen(),
      ),
      GoRoute(
        path: '/totp',
        builder: (context, state) => const AdminTotpScreen(),
      ),
      GoRoute(
        path: '/admin',
        builder: (context, state) => const AdminShellScreen(),
      ),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});

class PakiCarpoolAdminApp extends ConsumerWidget {
  const PakiCarpoolAdminApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(adminRouterProvider);
    if (Platform.isIOS) {
      return CupertinoApp.router(
        title: 'PakiCarpool Admin',
        theme: const CupertinoThemeData(primaryColor: AdminColors.teal),
        routerConfig: router,
        debugShowCheckedModeBanner: false,
      );
    }
    return MaterialApp.router(
      title: 'PakiCarpool Admin',
      theme: AdminTheme.light,
      darkTheme: AdminTheme.dark,
      themeMode: ThemeMode.system,
      routerConfig: router,
      debugShowCheckedModeBanner: false,
    );
  }
}

abstract final class AdminColors {
  static const canvas = Color(0xFFF5F8F6);
  static const ink = Color(0xFF1C2B30);
  static const teal = Color(0xFF087D85);
  static const saffron = Color(0xFFE7C64A);
  static const coral = Color(0xFFC14F45);
  static const muted = Color(0xFF44585D);
  static const outline = Color(0xFFC8D4D1);
  static const white = Colors.white;
}

abstract final class AdminTheme {
  static final light = ThemeData(
    useMaterial3: true,
    colorScheme: ColorScheme.fromSeed(seedColor: AdminColors.teal).copyWith(
      primary: AdminColors.teal,
      onPrimary: AdminColors.white,
      secondary: AdminColors.saffron,
      onSecondary: AdminColors.ink,
      surface: AdminColors.canvas,
      onSurface: AdminColors.ink,
      error: AdminColors.coral,
    ),
    scaffoldBackgroundColor: AdminColors.canvas,
    appBarTheme: const AppBarTheme(
      backgroundColor: AdminColors.canvas,
      foregroundColor: AdminColors.ink,
      elevation: 0,
      scrolledUnderElevation: 0,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: AdminColors.white,
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: AdminColors.outline),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: AdminColors.teal, width: 1.5),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(52),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      ),
    ),
  );

  static final dark = ThemeData(
    useMaterial3: true,
    colorScheme: ColorScheme.fromSeed(
      seedColor: AdminColors.teal,
      brightness: Brightness.dark,
    ),
  );
}
