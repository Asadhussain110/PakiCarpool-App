import 'package:flutter/material.dart';

abstract final class RouteAtlasColors {
  static const canvas = Color(0xFFF5F8F6);
  static const ink = Color(0xFF1C2B30);
  static const teal = Color(0xFF087D85);
  static const saffron = Color(0xFFE7C64A);
  static const coral = Color(0xFFC14F45);
  static const muted = Color(0xFF44585D);
  static const secondaryMuted = Color(0xFF657579);
  static const outline = Color(0xFFC8D4D1);
  static const white = Color(0xFFFFFFFF);
  static const tealSurface = Color(0xFFE1F2EF);
  static const errorSurface = Color(0xFFF8E9E7);
}

abstract final class RouteAtlasTheme {
  static final light = ThemeData(
    useMaterial3: true,
    colorScheme:
        ColorScheme.fromSeed(
          seedColor: RouteAtlasColors.teal,
          brightness: Brightness.light,
        ).copyWith(
          primary: RouteAtlasColors.teal,
          onPrimary: RouteAtlasColors.white,
          secondary: RouteAtlasColors.saffron,
          onSecondary: RouteAtlasColors.ink,
          surface: RouteAtlasColors.canvas,
          onSurface: RouteAtlasColors.ink,
          error: RouteAtlasColors.coral,
          onError: RouteAtlasColors.white,
        ),
    scaffoldBackgroundColor: RouteAtlasColors.canvas,
    appBarTheme: const AppBarTheme(
      backgroundColor: RouteAtlasColors.canvas,
      foregroundColor: RouteAtlasColors.ink,
      centerTitle: false,
      elevation: 0,
      scrolledUnderElevation: 0,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: RouteAtlasColors.white,
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 17),
      hintStyle: const TextStyle(color: RouteAtlasColors.secondaryMuted),
      labelStyle: const TextStyle(color: RouteAtlasColors.muted),
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: RouteAtlasColors.outline),
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: RouteAtlasColors.outline),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: RouteAtlasColors.teal, width: 1.5),
      ),
      errorBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: RouteAtlasColors.coral),
      ),
      focusedErrorBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: RouteAtlasColors.coral, width: 1.5),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(56),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        textStyle: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: RouteAtlasColors.teal,
        minimumSize: const Size(44, 44),
        textStyle: const TextStyle(fontWeight: FontWeight.w700),
      ),
    ),
  );

  static final dark = ThemeData(
    useMaterial3: true,
    colorScheme:
        ColorScheme.fromSeed(
          seedColor: RouteAtlasColors.teal,
          brightness: Brightness.dark,
        ).copyWith(
          primary: const Color(0xFF72D2CF),
          onPrimary: const Color(0xFF003638),
          secondary: RouteAtlasColors.saffron,
          onSecondary: RouteAtlasColors.ink,
          error: const Color(0xFFFFB4AA),
        ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 17),
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: Color(0xFF56696D)),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(8),
        borderSide: const BorderSide(color: Color(0xFF72D2CF), width: 1.5),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(56),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      ),
    ),
  );
}
