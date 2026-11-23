import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_client.dart';
import '../../core/network/mobile_api.dart';
import '../../core/realtime/signalr_service.dart';
import '../auth/models/app_session.dart';
import '../auth/models/auth_session.dart';
import '../driver/presentation/driver_screens.dart';
import '../passenger/presentation/passenger_screens.dart';
import 'screen_parts.dart';

class RoleHomeScreen extends ConsumerStatefulWidget {
  const RoleHomeScreen({super.key});

  @override
  ConsumerState<RoleHomeScreen> createState() => _RoleHomeScreenState();
}

class _RoleHomeScreenState extends ConsumerState<RoleHomeScreen> {
  int _selectedIndex = 0;
  bool _signingOut = false;
  String? _notificationError;
  StreamSubscription<Map<String, dynamic>>? _notificationSubscription;

  @override
  void initState() {
    super.initState();
    final realtime = ref.read(signalRServiceProvider);
    _notificationSubscription = realtime.notifications.listen((notification) {
      ref.read(notificationInboxProvider.notifier).add(notification);
    });
    _connectNotifications();
  }

  Future<void> _connectNotifications() async {
    try {
      await ref.read(signalRServiceProvider).connect();
      if (mounted) setState(() => _notificationError = null);
    } catch (_) {
      if (mounted) {
        setState(
          () => _notificationError =
              'Live notifications are unavailable. Retry when you are online.',
        );
      }
    }
  }

  @override
  void dispose() {
    _notificationSubscription?.cancel();
    super.dispose();
  }

  Future<void> _logout() async {
    setState(() => _signingOut = true);
    final messenger = ScaffoldMessenger.of(context);
    var logoutRequestFailed = false;
    try {
      await ref.read(mobileApiProvider).postData('auth/logout');
    } on ApiFailure {
      // Local credentials are cleared even if the revocation request cannot reach the API.
      logoutRequestFailed = true;
    } finally {
      try {
        await ref.read(signalRServiceProvider).stop();
      } catch (_) {}
      await ref.read(apiClientProvider).clearSession();
      ref.read(appSessionProvider.notifier).clear();
      ref.read(notificationInboxProvider.notifier).clear();
      if (mounted) {
        context.go('/login');
        if (logoutRequestFailed) {
          messenger.showSnackBar(
            const SnackBar(
              content: Text(
                'Signed out locally — server session may still be active.',
              ),
              duration: Duration(seconds: 3),
            ),
          );
        }
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(appSessionProvider);
    if (session == null) {
      ref.read(notificationInboxProvider.notifier).clear();
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) context.go('/login');
      });
      return const Scaffold(body: LoadingPane(label: 'Opening your account'));
    }

    final isPassenger = session.role == PakiRole.passenger;
    final labels = isPassenger
        ? const ['Rides', 'Booking', 'Alerts', 'Account']
        : const ['Home', 'Active', 'Car', 'Alerts', 'Account'];
    final icons = isPassenger
        ? const [
            Icons.alt_route,
            Icons.my_location,
            Icons.notifications_outlined,
            Icons.person_outline,
          ]
        : const [
            Icons.dashboard_outlined,
            Icons.route,
            Icons.directions_car_outlined,
            Icons.notifications_outlined,
            Icons.person_outline,
          ];
    final child = isPassenger
        ? switch (_selectedIndex) {
            0 => const PassengerRideListScreen(),
            1 => ActiveBookingScreen(
              onBrowseRides: () => setState(() => _selectedIndex = 0),
            ),
            2 => NotificationInboxScreen(
              connectionError: _notificationError,
              onRetry: _connectNotifications,
            ),
            _ => _AccountScreen(
              session: session,
              onSignOut: _logout,
              signingOut: _signingOut,
            ),
          }
        : switch (_selectedIndex) {
            0 => const DriverDashboardScreen(),
            1 => ActiveRideScreen(
              onReturnToDashboard: () => setState(() => _selectedIndex = 0),
            ),
            2 => const CarRegistrationScreen(),
            3 => NotificationInboxScreen(
              connectionError: _notificationError,
              onRetry: _connectNotifications,
            ),
            _ => _AccountScreen(
              session: session,
              onSignOut: _logout,
              signingOut: _signingOut,
            ),
          };

    return Scaffold(
      appBar: AppBar(title: Text(isPassenger ? 'PakiCarpool' : 'Driver')),
      body: child,
      bottomNavigationBar: NavigationBar(
        selectedIndex: _selectedIndex,
        onDestinationSelected: (index) =>
            setState(() => _selectedIndex = index),
        destinations: [
          for (var index = 0; index < labels.length; index++)
            NavigationDestination(
              icon: Icon(icons[index]),
              label: labels[index],
            ),
        ],
      ),
    );
  }
}

class NotificationInboxScreen extends ConsumerWidget {
  const NotificationInboxScreen({
    this.connectionError,
    this.onRetry,
    super.key,
  });

  final String? connectionError;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifications = ref.watch(notificationInboxProvider);
    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
      children: [
        Text('Notifications', style: Theme.of(context).textTheme.headlineSmall),
        if (connectionError != null) ...[
          const SizedBox(height: 12),
          NoticeStrip(message: connectionError!, isError: true),
          if (onRetry != null)
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                onPressed: onRetry,
                icon: const Icon(Icons.refresh),
                label: const Text('Retry connection'),
              ),
            ),
        ],
        if (notifications.isEmpty)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 56),
            child: Column(
              children: [
                Icon(Icons.notifications_none_outlined, size: 42),
                SizedBox(height: 12),
                Text('No notifications yet.'),
                SizedBox(height: 4),
                Text('New live alerts will appear here during this session.'),
              ],
            ),
          )
        else
          for (final notification in notifications.reversed)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.notifications_outlined),
              title: Text(notificationDisplayMessage(notification)),
              subtitle: Text(_notificationTime(notification['receivedAt'])),
            ),
      ],
    );
  }
}

String _notificationTime(Object? value) {
  final receivedAt = DateTime.tryParse(value?.toString() ?? '')?.toLocal();
  if (receivedAt == null) return 'Just now';
  return '${receivedAt.year}-${receivedAt.month.toString().padLeft(2, '0')}-${receivedAt.day.toString().padLeft(2, '0')} '
      '${receivedAt.hour.toString().padLeft(2, '0')}:${receivedAt.minute.toString().padLeft(2, '0')}';
}

class _AccountScreen extends StatelessWidget {
  const _AccountScreen({
    required this.session,
    required this.onSignOut,
    required this.signingOut,
  });

  final AuthSession session;
  final VoidCallback onSignOut;
  final bool signingOut;

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(20),
    children: [
      Text('Account', style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 18),
      ListTile(
        contentPadding: EdgeInsets.zero,
        leading: const Icon(Icons.person_outline),
        title: const Text('Name'),
        subtitle: Text(session.fullName),
      ),
      ListTile(
        contentPadding: EdgeInsets.zero,
        leading: const Icon(Icons.email_outlined),
        title: const Text('Email'),
        subtitle: Text(session.email),
      ),
      ListTile(
        contentPadding: EdgeInsets.zero,
        leading: const Icon(Icons.badge_outlined),
        title: const Text('Role'),
        subtitle: Text(session.role.apiValue),
      ),
      const Divider(height: 20),
      const SizedBox(height: 22),
      OutlinedButton.icon(
        onPressed: signingOut ? null : onSignOut,
        icon: const Icon(Icons.logout),
        label: Text(signingOut ? 'Signing out…' : 'Sign out'),
      ),
    ],
  );
}
