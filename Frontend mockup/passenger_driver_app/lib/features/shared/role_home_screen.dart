import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_client.dart';
import '../../core/network/mobile_api.dart';
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

  Future<void> _logout() async {
    setState(() => _signingOut = true);
    try {
      await ref.read(mobileApiProvider).postData('auth/logout');
    } on ApiFailure {
      // Local credentials are cleared even if the revocation request cannot reach the API.
    } finally {
      await ref.read(apiClientProvider).clearSession();
      ref.read(appSessionProvider.notifier).clear();
      if (mounted) context.go('/login');
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(appSessionProvider);
    if (session == null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) context.go('/login');
      });
      return const Scaffold(body: LoadingPane(label: 'Opening your account'));
    }

    final isPassenger = session.role == PakiRole.passenger;
    final labels = isPassenger
        ? const ['Rides', 'Booking', 'Account']
        : const ['Overview', 'Active ride', 'Car'];
    final icons = isPassenger
        ? const [Icons.alt_route, Icons.my_location, Icons.person_outline]
        : const [
            Icons.dashboard_outlined,
            Icons.route,
            Icons.directions_car_outlined,
          ];
    final child = isPassenger
        ? switch (_selectedIndex) {
            0 => const PassengerRideListScreen(),
            1 => const ActiveBookingScreen(),
            _ => _AccountScreen(
              session: session,
              onSignOut: _logout,
              signingOut: _signingOut,
            ),
          }
        : switch (_selectedIndex) {
            0 => const DriverDashboardScreen(),
            1 => const ActiveRideScreen(),
            _ => const CarRegistrationScreen(),
          };

    return Scaffold(
      appBar: AppBar(
        title: Text(isPassenger ? 'PakiCarpool' : 'Driver'),
        actions: [
          if (isPassenger)
            IconButton(
              tooltip: 'Account',
              onPressed: () => setState(() => _selectedIndex = 2),
              icon: const Icon(Icons.person_outline),
            ),
        ],
      ),
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
      Text(session.fullName, style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 5),
      Text(session.email),
      const SizedBox(height: 8),
      StateLabel(session.role.apiValue),
      const Divider(height: 36),
      const Text('Role is fixed to the account created at registration.'),
      const SizedBox(height: 22),
      OutlinedButton.icon(
        onPressed: signingOut ? null : onSignOut,
        icon: const Icon(Icons.logout),
        label: Text(signingOut ? 'Signing out…' : 'Sign out'),
      ),
    ],
  );
}
