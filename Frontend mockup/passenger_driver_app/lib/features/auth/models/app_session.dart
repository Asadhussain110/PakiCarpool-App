import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'auth_session.dart';

final appSessionProvider = NotifierProvider<AppSessionController, AuthSession?>(
  AppSessionController.new,
);

class AppSessionController extends Notifier<AuthSession?> {
  static AuthSession? restoredSession;

  @override
  AuthSession? build() => restoredSession;

  void set(AuthSession session) {
    restoredSession = session;
    state = session;
  }

  void clear() {
    restoredSession = null;
    state = null;
  }

  void restore(AuthSession? session) {
    restoredSession = session;
    state = session;
  }
}
