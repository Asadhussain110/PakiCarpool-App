import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:go_router/go_router.dart';
import 'package:path_provider/path_provider.dart';
import 'package:signalr_netcore/signalr_client.dart';

import 'admin_api.dart';
import 'main.dart';

class AdminLoginScreen extends ConsumerStatefulWidget {
  const AdminLoginScreen({
    this.sessionExpired = false,
    this.actionFailed = false,
    super.key,
  });

  final bool sessionExpired;
  final bool actionFailed;

  @override
  ConsumerState<AdminLoginScreen> createState() => _AdminLoginScreenState();
}

class _AdminLoginScreenState extends ConsumerState<AdminLoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _serverAddress = TextEditingController(text: adminApiBaseUrl);
  bool _submitting = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _serverAddress.dispose();
    super.dispose();
  }

  Future<void> _saveServerAddress() async {
    try {
      final address = normalizeAdminApiBaseUrl(_serverAddress.text);
      await const FlutterSecureStorage().write(
        key: adminApiBaseUrlStorageKey,
        value: address,
      );
      ref.read(adminApiProvider).setBaseUrl(address);
      if (mounted) {
        setState(() {
          _serverAddress.text = address;
          _error = null;
        });
      }
    } on FormatException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } on Exception catch (_) {
      if (mounted) {
        setState(() => _error = 'Server address could not be saved.');
      }
    }
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final partial = await ref
          .read(adminApiProvider)
          .login(email: _email.text.trim(), password: _password.text);
      ref.read(adminPartialTokenProvider.notifier).set(partial);
      if (mounted) context.go('/totp');
    } on AdminApiError catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 430),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const _AdminWordmark(),
                  const SizedBox(height: 36),
                  Text(
                    'Admin sign in',
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  const SizedBox(height: 8),
                  const Text('Authorized operations accounts only.'),
                  if (localBuild) ...[
                    const SizedBox(height: 18),
                    TextFormField(
                      controller: _serverAddress,
                      keyboardType: TextInputType.url,
                      autocorrect: false,
                      decoration: const InputDecoration(
                        labelText: 'Server address',
                        hintText: 'http://192.168.1.10:58240/api/v1',
                      ),
                    ),
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton(
                        onPressed: _saveServerAddress,
                        child: const Text('Save server address'),
                      ),
                    ),
                  ],
                  if (widget.sessionExpired) ...[
                    const SizedBox(height: 14),
                    _AdminNotice(
                      message: widget.actionFailed
                          ? 'Your session has expired. Please re-authenticate. The action did not complete; please retry it after re-authenticating.'
                          : 'Your session has expired. Please re-authenticate.',
                      error: true,
                    ),
                  ],
                  const SizedBox(height: 24),
                  TextFormField(
                    controller: _email,
                    keyboardType: TextInputType.emailAddress,
                    autofillHints: const [AutofillHints.username],
                    validator: (value) => (value?.contains('@') ?? false)
                        ? null
                        : 'Enter your administrator email.',
                    decoration: const InputDecoration(labelText: 'Email'),
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _password,
                    obscureText: true,
                    autofillHints: const [AutofillHints.password],
                    validator: (value) => (value?.isNotEmpty ?? false)
                        ? null
                        : 'Enter your password.',
                    onFieldSubmitted: (_) => _submit(),
                    decoration: const InputDecoration(labelText: 'Password'),
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 14),
                    _AdminNotice(message: _error!, error: true),
                  ],
                  const SizedBox(height: 20),
                  FilledButton(
                    onPressed: _submitting ? null : _submit,
                    child: _submitting
                        ? const SizedBox.square(
                            dimension: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Continue to verification'),
                  ),
                  Align(
                    alignment: Alignment.center,
                    child: TextButton(
                      onPressed: () => context.push('/forgot-password'),
                      child: const Text('Forgot your password?'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

class AdminForgotPasswordScreen extends ConsumerStatefulWidget {
  const AdminForgotPasswordScreen({super.key});

  @override
  ConsumerState<AdminForgotPasswordScreen> createState() =>
      _AdminForgotPasswordScreenState();
}

class _AdminForgotPasswordScreenState
    extends ConsumerState<AdminForgotPasswordScreen> {
  final _email = TextEditingController();
  bool _submitting = false;
  String? _message;
  bool _failed = false;

  @override
  void dispose() {
    _email.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_email.text.contains('@'))) {
      setState(() {
        _message = 'Enter a valid administrator email.';
        _failed = true;
      });
      return;
    }
    setState(() {
      _submitting = true;
      _message = null;
    });
    try {
      await ref
          .read(adminApiProvider)
          .post(
            'auth/admin/forgot-password',
            body: {'email': _email.text.trim()},
          );
      if (mounted) {
        context.go(
          '/reset-password?email=${Uri.encodeQueryComponent(_email.text.trim())}',
        );
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _message = error.message;
          _failed = true;
        });
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Reset administrator password')),
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 430),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const _AdminWordmark(),
                const SizedBox(height: 28),
                const Text(
                  'Enter the email address for your administrator account.',
                ),
                const SizedBox(height: 18),
                TextField(
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                  decoration: const InputDecoration(labelText: 'Email'),
                ),
                if (_message != null) ...[
                  const SizedBox(height: 14),
                  _AdminNotice(message: _message!, error: _failed),
                ],
                const SizedBox(height: 18),
                FilledButton(
                  onPressed: _submitting ? null : _submit,
                  child: _submitting
                      ? const CircularProgressIndicator()
                      : const Text('Send reset code'),
                ),
                TextButton(
                  onPressed: () => context.go('/login'),
                  child: const Text('Back to admin sign in'),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

class AdminResetPasswordScreen extends ConsumerStatefulWidget {
  const AdminResetPasswordScreen({required this.email, super.key});

  final String email;

  @override
  ConsumerState<AdminResetPasswordScreen> createState() =>
      _AdminResetPasswordScreenState();
}

class _AdminResetPasswordScreenState
    extends ConsumerState<AdminResetPasswordScreen> {
  final _code = TextEditingController();
  final _password = TextEditingController();
  final _confirm = TextEditingController();
  final _totp = TextEditingController();
  Timer? _resendTimer;
  int _resendRemaining = 0;
  bool _submitting = false;
  bool _resending = false;
  bool _completed = false;
  String? _error;
  String? _notice =
      'If this email belongs to an eligible admin account, a code will be sent if request limits allow.';

  @override
  void dispose() {
    _resendTimer?.cancel();
    _code.dispose();
    _password.dispose();
    _confirm.dispose();
    _totp.dispose();
    super.dispose();
  }

  String? _passwordError(String value) {
    if (value.length < 8 || value.length > 128) {
      return 'Use 8 to 128 characters.';
    }
    if (!RegExp(r'[a-z]').hasMatch(value) ||
        !RegExp(r'[A-Z]').hasMatch(value) ||
        !RegExp(r'\d').hasMatch(value) ||
        !RegExp(r'[^A-Za-z0-9]').hasMatch(value)) {
      return 'Add uppercase, lowercase, a number, and a symbol.';
    }
    return null;
  }

  Future<void> _submit() async {
    if (!RegExp(r'^\d{6}$').hasMatch(_code.text)) {
      setState(() => _error = 'Enter the 6-digit code from your email.');
      return;
    }
    if (!RegExp(r'^\d{6}$').hasMatch(_totp.text)) {
      setState(
        () => _error = 'Enter the 6-digit code from your authenticator.',
      );
      return;
    }
    final validation = _passwordError(_password.text);
    if (validation != null) {
      setState(() => _error = validation);
      return;
    }
    if (_password.text != _confirm.text) {
      setState(() => _error = 'Passwords do not match.');
      return;
    }
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      await ref
          .read(adminApiProvider)
          .post(
            'auth/admin/reset-password',
            body: {
              'email': widget.email,
              'code': _code.text,
              'newPassword': _password.text,
              'totpCode': _totp.text,
            },
          );
      if (mounted) {
        setState(() => _completed = true);
      }
    } on AdminApiError catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  Future<void> _resendCode() async {
    setState(() {
      _resending = true;
      _error = null;
      _notice = null;
    });
    try {
      await ref
          .read(adminApiProvider)
          .post('auth/admin/forgot-password', body: {'email': widget.email});
      if (mounted) {
        setState(() {
          _notice = 'If eligible and within the request limit, a new reset code has been sent.';
          _resendRemaining = 60;
        });
        _resendTimer?.cancel();
        _resendTimer = Timer.periodic(const Duration(seconds: 1), (timer) {
          if (!mounted || _resendRemaining <= 1) {
            timer.cancel();
            if (mounted) setState(() => _resendRemaining = 0);
          } else {
            setState(() => _resendRemaining--);
          }
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _resending = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Reset administrator password')),
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 430),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const _AdminWordmark(),
                const SizedBox(height: 28),
                if (_completed) ...[
                  const _AdminNotice(
                    message: 'Your password has been reset. Sign in with your new password and authenticator code.',
                    error: false,
                  ),
                  const SizedBox(height: 18),
                  FilledButton(
                    onPressed: () => context.go('/login'),
                    child: const Text('Return to admin sign in'),
                  ),
                ] else ...[
                  Text('Enter the reset code requested for ${widget.email}.'),
                  const SizedBox(height: 8),
                  const Text(
                    'The email code expires in 10 minutes. Your authenticator code is also required.',
                  ),
                  const SizedBox(height: 18),
                  TextField(
                    controller: _code,
                    keyboardType: TextInputType.number,
                    inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                    maxLength: 6,
                    onChanged: (_) => setState(() => _error = null),
                    decoration: const InputDecoration(
                      labelText: 'Email reset code',
                      counterText: '',
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _password,
                    obscureText: true,
                    onChanged: (_) => setState(() => _error = null),
                    decoration: const InputDecoration(
                      labelText: 'New password',
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _confirm,
                    obscureText: true,
                    onChanged: (_) => setState(() => _error = null),
                    decoration: const InputDecoration(
                      labelText: 'Confirm new password',
                    ),
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'Use uppercase, lowercase, a number, and a symbol.',
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _totp,
                    keyboardType: TextInputType.number,
                    inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                    maxLength: 6,
                    onChanged: (_) => setState(() => _error = null),
                    decoration: const InputDecoration(
                      labelText: 'Authenticator code',
                      counterText: '',
                    ),
                  ),
                ],
                if (_error != null) ...[
                  const SizedBox(height: 14),
                  _AdminNotice(message: _error!, error: true),
                ],
                if (_notice != null) ...[
                  const SizedBox(height: 14),
                  _AdminNotice(message: _notice!, error: false),
                ],
                if (!_completed) ...[
                  const SizedBox(height: 18),
                  FilledButton(
                    onPressed: _submitting ? null : _submit,
                    child: _submitting
                        ? const CircularProgressIndicator()
                        : const Text('Reset password'),
                  ),
                  TextButton(
                    onPressed: _resending || _resendRemaining > 0
                        ? null
                        : _resendCode,
                    child: Text(
                      _resending
                          ? 'Sending code…'
                          : _resendRemaining > 0
                          ? 'Resend code in ${_resendRemaining}s'
                          : 'Resend code',
                    ),
                  ),
                  TextButton(
                    onPressed: () => context.go('/login'),
                    child: const Text('Return to admin sign in'),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

class AdminTotpScreen extends ConsumerStatefulWidget {
  const AdminTotpScreen({super.key});

  @override
  ConsumerState<AdminTotpScreen> createState() => _AdminTotpScreenState();
}

class _AdminTotpScreenState extends ConsumerState<AdminTotpScreen> {
  final _code = TextEditingController();
  Timer? _timer;
  int _remaining = 30;
  bool _submitting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) {
        setState(() => _remaining = 30 - (DateTime.now().second % 30));
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _code.dispose();
    super.dispose();
  }

  Future<void> _verify() async {
    final token = ref.read(adminPartialTokenProvider);
    if (token == null) {
      context.go('/login');
      return;
    }
    if (!RegExp(r'^\d{6}$').hasMatch(_code.text)) {
      setState(
        () => _error = 'Enter the 6-digit code from your authenticator.',
      );
      return;
    }
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final session = await ref
          .read(adminApiProvider)
          .verifyTotp(code: _code.text, partialToken: token);
      ref.read(adminPartialTokenProvider.notifier).set(null);
      ref.read(adminSessionProvider.notifier).set(session);
      ref.read(adminExpiryNoticeProvider.notifier).clear();
      ref.read(adminApiProvider).resumeSession();
      if (mounted) context.go('/admin');
    } on AdminApiError catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (ref.watch(adminPartialTokenProvider) == null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) context.go('/login');
      });
    }
    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          tooltip: 'Back to credentials',
          onPressed: () {
            ref.read(adminPartialTokenProvider.notifier).set(null);
            context.go('/login');
          },
          icon: const Icon(Icons.arrow_back),
        ),
        title: const Text('Two-step verification'),
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 430),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'Enter your code',
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'Use the current 6-digit code from your authenticator app.',
                  ),
                  const SizedBox(height: 22),
                  TextField(
                    controller: _code,
                    keyboardType: TextInputType.number,
                    maxLength: 6,
                    inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 26, letterSpacing: 7),
                    decoration: const InputDecoration(
                      hintText: '000000',
                      counterText: '',
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Code refreshes in $_remaining seconds.',
                    textAlign: TextAlign.center,
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 14),
                    _AdminNotice(message: _error!, error: true),
                  ],
                  const SizedBox(height: 20),
                  FilledButton(
                    onPressed: _submitting ? null : _verify,
                    child: _submitting
                        ? const SizedBox.square(
                            dimension: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Verify and open admin'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class AdminShellScreen extends ConsumerStatefulWidget {
  const AdminShellScreen({super.key});

  @override
  ConsumerState<AdminShellScreen> createState() => _AdminShellScreenState();
}

class _AdminShellScreenState extends ConsumerState<AdminShellScreen> {
  int _selectedIndex = 0;
  HubConnection? _hub;
  String? _liveAlert;
  final List<Map<String, dynamic>> _notifications = [];
  Timer? _sessionTimer;
  DateTime _sessionClock = DateTime.now().toUtc();

  static const _labels = [
    'Overview',
    'Approvals',
    'Liveness',
    'Users',
    'Rides',
    'Flags',
    'Reports',
    'Notifications',
  ];
  static const _icons = [
    Icons.dashboard_outlined,
    Icons.fact_check_outlined,
    Icons.face_retouching_natural_outlined,
    Icons.people_outline,
    Icons.route_outlined,
    Icons.flag_outlined,
    Icons.query_stats_outlined,
    Icons.notifications_outlined,
  ];

  void _recordNotification(String title, String message) {
    _notifications.add({
      'title': title,
      'message': message,
      'receivedAt': DateTime.now().toUtc().toIso8601String(),
    });
    if (_notifications.length > 100) _notifications.removeAt(0);
  }

  @override
  void initState() {
    super.initState();
    _connectAlerts();
    _sessionTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      final session = ref.read(adminSessionProvider);
      final expiresAt = session?.expiresAt;
      final now = DateTime.now().toUtc();
      if (session != null && (expiresAt == null || !now.isBefore(expiresAt))) {
        final api = ref.read(adminApiProvider);
        api.expireSession(actionFailed: api.hasInFlightActions);
      }
      if (mounted) setState(() => _sessionClock = now);
    });
  }

  Future<void> _connectAlerts() async {
    final api = ref
        .read(adminApiProvider)
        .baseUrl
        .replaceFirst('/api/v1', '/hubs/pakicarpool')
        .replaceAll(RegExp(r'/+$'), '');
    final connection = HubConnectionBuilder()
        .withUrl(
          api,
          options: HttpConnectionOptions(
            accessTokenFactory: () async =>
                ref.read(adminSessionProvider)?.accessToken ?? '',
          ),
        )
        .withAutomaticReconnect(retryDelays: [2000, 5000, 10000, 20000, 30000])
        .build();
    connection.on('LivenessFailureAlert', (args) {
      final payload = args == null || args.isEmpty
          ? const <String, dynamic>{}
          : _map(args.first);
      if (mounted) {
        final message =
            'Liveness review: ${payload['driverName'] ?? 'A driver'} needs review.';
        setState(() {
          _liveAlert = message;
          _recordNotification('Liveness review', message);
        });
      }
    });
    connection.on('ReceiveNotification', (args) {
      final payload = args == null || args.isEmpty
          ? const <String, dynamic>{}
          : _map(args.first);
      if (mounted) {
        final title = payload['title']?.toString() ?? 'Admin alert';
        final message = payload['message']?.toString() ?? '';
        setState(() {
          _liveAlert = '$title: $message';
          _recordNotification(title, message);
        });
      }
    });
    connection.onclose(({Exception? error}) {
      if (mounted) {
        setState(
          () => _liveAlert =
              'Live alerts disconnected. Refresh dashboard data manually.',
        );
      }
    });
    _hub = connection;
    try {
      await connection.start();
    } catch (_) {
      if (mounted) setState(() => _liveAlert = 'Live alerts unavailable.');
    }
  }

  Future<void> _signOut() async {
    final hub = _hub;
    _hub = null;
    String? logoutError;
    try {
      await ref.read(adminApiProvider).post('auth/logout');
    } on AdminApiError catch (error) {
      logoutError = error.message;
    } finally {
      if (hub?.state == HubConnectionState.Connected) {
        try {
          await hub!.stop();
        } catch (error) {
          logoutError ??= 'Live alerts could not be disconnected: $error';
        }
      }
      ref.read(adminPartialTokenProvider.notifier).set(null);
      ref.read(adminSessionProvider.notifier).clear();
      if (mounted) {
        context.go('/login');
        if (logoutError != null) {
          _toast(
            context,
            'Signed out locally; server session may still be active. $logoutError',
            error: true,
          );
        }
      }
    }
  }

  @override
  void dispose() {
    _sessionTimer?.cancel();
    final hub = _hub;
    if (hub?.state == HubConnectionState.Connected) unawaited(hub!.stop());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(adminSessionProvider);
    if (session == null) {
      final actionFailed = ref.watch(adminExpiryNoticeProvider);
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        context.go(
          actionFailed == null
              ? '/login'
              : '/login?expired=1&actionFailed=${actionFailed ? 1 : 0}',
        );
      });
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    final pages = [
      AdminDashboardScreen(
        onOpen: (index) => setState(() => _selectedIndex = index),
      ),
      const DriverApprovalsScreen(),
      const AdminLivenessReviewScreen(),
      const AdminUsersScreen(),
      const AdminRidesScreen(),
      const AdminFlagsScreen(),
      const AdminReportsScreen(),
      AdminNotificationsScreen(notifications: _notifications),
    ];
    final wide = MediaQuery.sizeOf(context).width >= 720;
    final remaining = session.expiresAt?.difference(_sessionClock);
    final showCountdown =
        remaining != null &&
        remaining > Duration.zero &&
        remaining < const Duration(minutes: 3);
    final body = Column(
      children: [
        if (_liveAlert != null)
          MaterialBanner(
            content: Text(_liveAlert!),
            leading: const Icon(Icons.notifications_active_outlined),
            actions: [
              TextButton(
                onPressed: () => setState(() => _liveAlert = null),
                child: const Text('Dismiss'),
              ),
            ],
          ),
        Expanded(child: pages[_selectedIndex]),
      ],
    );
    return Scaffold(
      appBar: AppBar(
        title: Text(_labels[_selectedIndex]),
        actions: [
          if (!showCountdown)
            Padding(
              padding: const EdgeInsets.only(right: 8),
              child: Center(child: Text(session.fullName)),
            ),
          if (showCountdown)
            Padding(
              padding: const EdgeInsets.only(right: 8),
              child: Center(
                child: Text(
                  'Session expires in ${remaining.inMinutes}:${(remaining.inSeconds % 60).toString().padLeft(2, '0')}',
                  style: const TextStyle(fontSize: 12),
                ),
              ),
            ),
          IconButton(
            tooltip: 'Sign out',
            onPressed: _signOut,
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: wide
          ? Row(
              children: [
                NavigationRail(
                  selectedIndex: _selectedIndex,
                  labelType: NavigationRailLabelType.all,
                  onDestinationSelected: (value) =>
                      setState(() => _selectedIndex = value),
                  destinations: [
                    for (var index = 0; index < _labels.length; index++)
                      NavigationRailDestination(
                        icon: Icon(_icons[index]),
                        label: Text(_labels[index]),
                      ),
                  ],
                ),
                const VerticalDivider(width: 1),
                Expanded(child: body),
              ],
            )
          : body,
      bottomNavigationBar: wide
          ? null
          : NavigationBar(
              selectedIndex: _selectedIndex,
              onDestinationSelected: (value) =>
                  setState(() => _selectedIndex = value),
              destinations: [
                for (var index = 0; index < _labels.length; index++)
                  NavigationDestination(
                    icon: Icon(_icons[index]),
                    label: _labels[index],
                  ),
              ],
            ),
    );
  }
}

class AdminNotificationsScreen extends StatelessWidget {
  const AdminNotificationsScreen({required this.notifications, super.key});

  final List<Map<String, dynamic>> notifications;

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
    children: [
      Text('Notifications', style: Theme.of(context).textTheme.headlineSmall),
      if (notifications.isEmpty)
        const Padding(
          padding: EdgeInsets.symmetric(vertical: 56),
          child: Column(
            children: [
              Icon(Icons.notifications_none_outlined, size: 42),
              SizedBox(height: 12),
              Text('No notifications yet.'),
              SizedBox(height: 4),
              Text(
                'Live operations alerts will appear here during this session.',
              ),
            ],
          ),
        )
      else
        for (final notification in notifications.reversed)
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.notifications_outlined),
            title: Text(notification['title']?.toString() ?? 'Admin alert'),
            subtitle: Text(
              '${notification['message'] ?? ''}\n${_dateTime(notification['receivedAt'])}',
            ),
            isThreeLine: true,
          ),
    ],
  );
}

class AdminDashboardScreen extends ConsumerStatefulWidget {
  const AdminDashboardScreen({required this.onOpen, super.key});

  final ValueChanged<int> onOpen;

  @override
  ConsumerState<AdminDashboardScreen> createState() =>
      _AdminDashboardScreenState();
}

class _AdminDashboardScreenState extends ConsumerState<AdminDashboardScreen> {
  Map<String, dynamic>? _metrics;
  String? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await ref.read(adminApiProvider).get('admin/dashboard');
      if (mounted) {
        setState(() {
          _metrics = data;
          _loading = false;
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const _AdminLoading();
    if (_error != null) return _AdminError(message: _error!, onRetry: _load);
    final metrics = _metrics ?? const <String, dynamic>{};
    final rows = [
      ('Users', metrics['totalUsers'], Icons.people_outline),
      ('Drivers', metrics['totalDrivers'], Icons.directions_car_outlined),
      ('Passengers', metrics['totalPassengers'], Icons.person_outline),
      ('All rides', metrics['totalRides'], Icons.route_outlined),
      ('Active rides', metrics['activeRides'], Icons.alt_route),
      (
        'Pending cars',
        metrics['pendingCarApprovals'],
        Icons.fact_check_outlined,
      ),
      ('Open flags', metrics['openFlags'], Icons.flag_outlined),
      (
        'Suspended accounts',
        metrics['suspendedAccounts'],
        Icons.block_outlined,
      ),
    ];
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            'Operations overview',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 5),
          const Text('Review trust and platform activity from one place.'),
          const SizedBox(height: 20),
          for (var index = 0; index < rows.length; index += 2)
            Row(
              children: [
                Expanded(
                  child: _MetricLine(
                    label: rows[index].$1,
                    value: rows[index].$2,
                    icon: rows[index].$3,
                  ),
                ),
                const SizedBox(width: 18),
                Expanded(
                  child: _MetricLine(
                    label: rows[index + 1].$1,
                    value: rows[index + 1].$2,
                    icon: rows[index + 1].$3,
                  ),
                ),
              ],
            ),
          const SizedBox(height: 24),
          Text(
            'Priority queues',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.fact_check_outlined),
            title: const Text('Pending vehicle approvals'),
            trailing: Text('${metrics['pendingCarApprovals'] ?? 0}'),
            onTap: () => widget.onOpen(1),
          ),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.flag_outlined),
            title: const Text('Open flags'),
            trailing: Text('${metrics['openFlags'] ?? 0}'),
            onTap: () => widget.onOpen(4),
          ),
          const Divider(height: 26),
          const Text(
            'Historical activity feed is unavailable from the current API. Live SignalR alerts appear while this app session is open.',
            style: TextStyle(color: AdminColors.muted),
          ),
        ],
      ),
    );
  }
}

class AdminLivenessReviewScreen extends ConsumerStatefulWidget {
  const AdminLivenessReviewScreen({super.key});

  @override
  ConsumerState<AdminLivenessReviewScreen> createState() =>
      _AdminLivenessReviewScreenState();
}

class _AdminLivenessReviewScreenState
    extends ConsumerState<AdminLivenessReviewScreen> {
  List<Map<String, dynamic>> _reviews = const [];
  int _page = 1;
  int _totalPages = 1;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({int page = 1}) async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await ref
          .read(adminApiProvider)
          .get('admin/liveness-reviews', query: {'page': page, 'pageSize': 20});
      final items = data['items'];
      if (items is! List) {
        throw const AdminApiError(
          'The server returned an invalid liveness review page.',
        );
      }
      final totalPages = (data['totalPages'] as num?)?.toInt() ?? 1;
      if (!mounted) return;
      if (totalPages > 0 && page > totalPages) {
        await _load(page: totalPages);
        return;
      }
      if (mounted) {
        setState(() {
          _reviews = items
              .whereType<Map>()
              .map((item) => Map<String, dynamic>.from(item))
              .toList();
          _page = (data['page'] as num?)?.toInt() ?? page;
          _totalPages = totalPages;
          _loading = false;
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  Future<void> _reject(Map<String, dynamic> review) async {
    final reason = await _prompt(
      context,
      title: 'Reject liveness submission',
      label: 'Reason shown to the driver (10–500 characters)',
      minLength: 10,
    );
    if (reason == null || !mounted) return;
    final id = review['driverId'];
    if (id == null) {
      _toast(context, 'The review has no driver ID.', error: true);
      return;
    }
    try {
      await ref
          .read(adminApiProvider)
          .put('admin/drivers/$id/liveness/reject', body: {'reason': reason});
      if (mounted) _toast(context, 'Liveness submission rejected.');
      await _load(page: _page);
    } on AdminApiError catch (error) {
      if (mounted) _toast(context, error.message, error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const _AdminLoading();
    if (_error != null) {
      return _AdminError(
        message: _error!,
        onRetry: () => _load(page: _page),
      );
    }
    return RefreshIndicator(
      onRefresh: () => _load(page: _page),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            'Liveness review queue',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 5),
          const Text(
            'Review liveness failure alerts and reject stale submissions. Drivers must complete a fresh server-verified challenge.',
          ),
          const SizedBox(height: 18),
          if (_reviews.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 60),
              child: Center(child: Text('No driver liveness reviews pending.')),
            )
          else
            for (final review in _reviews)
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: SizedBox(
                  width: 52,
                  height: 52,
                  child: review['selfieUrl']?.toString().isNotEmpty == true
                      ? ClipRRect(
                          borderRadius: BorderRadius.circular(8),
                          child: Image.network(
                            review['selfieUrl'].toString(),
                            fit: BoxFit.cover,
                            errorBuilder: (context, error, stackTrace) =>
                                const Icon(
                                  Icons.face_retouching_natural_outlined,
                                ),
                          ),
                        )
                      : const Icon(Icons.face_retouching_natural_outlined),
                ),
                title: Text(
                  '${review['driverName'] ?? 'Driver'} · Admin review',
                ),
                subtitle: Text(
                  '${review['email'] ?? ''}\n'
                  'Submitted ${review['submittedAt'] ?? 'date unavailable'}',
                ),
                trailing: IconButton(
                  tooltip: 'Reject submission',
                  onPressed: () => _reject(review),
                  icon: const Icon(Icons.close),
                ),
              ),
          if (_totalPages > 1)
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                IconButton(
                  tooltip: 'Previous page',
                  onPressed: _page > 1 ? () => _load(page: _page - 1) : null,
                  icon: const Icon(Icons.chevron_left),
                ),
                Text('Page $_page of $_totalPages'),
                IconButton(
                  tooltip: 'Next page',
                  onPressed: _page < _totalPages
                      ? () => _load(page: _page + 1)
                      : null,
                  icon: const Icon(Icons.chevron_right),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

class DriverApprovalsScreen extends ConsumerStatefulWidget {
  const DriverApprovalsScreen({super.key});

  @override
  ConsumerState<DriverApprovalsScreen> createState() =>
      _DriverApprovalsScreenState();
}

class _DriverApprovalsScreenState extends ConsumerState<DriverApprovalsScreen> {
  List<Map<String, dynamic>> _cars = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final cars = await ref
          .read(adminApiProvider)
          .getList('admin/driver-approvals');
      if (mounted) {
        setState(() {
          _cars = cars;
          _loading = false;
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  Future<void> _approve(Map<String, dynamic> car) async {
    try {
      await ref
          .read(adminApiProvider)
          .put('admin/driver-approvals/${car['id']}/approve');
      if (mounted) _toast(context, 'Vehicle approved.');
      await _load();
    } on AdminApiError catch (error) {
      if (mounted) {
        _toast(context, error.message, error: true);
      }
    }
  }

  Future<void> _reject(Map<String, dynamic> car) async {
    final reason = await _prompt(
      context,
      title: 'Reject vehicle',
      label: 'Reason sent to the driver (10–500 characters)',
      minLength: 10,
    );
    if (reason == null || !mounted) return;
    try {
      await ref
          .read(adminApiProvider)
          .put(
            'admin/driver-approvals/${car['id']}/reject',
            body: {'rejectionReason': reason},
          );
      if (mounted) _toast(context, 'Vehicle rejected.');
      await _load();
    } on AdminApiError catch (error) {
      if (mounted) {
        _toast(context, error.message, error: true);
      }
    }
  }

  Future<void> _viewRegistrationDocument(Map<String, dynamic> car) async {
    try {
      final result = await ref
          .read(adminApiProvider)
          .get('admin/driver-approvals/${car['id']}/registration-document');
      final url = result['url']?.toString();
      if (url == null || url.isEmpty) {
        throw const AdminApiError(
          'The server did not return a registration document URL.',
        );
      }
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (context) => Dialog(
          child: InteractiveViewer(
            child: Image.network(
              url,
              errorBuilder: (context, error, stackTrace) => const Padding(
                padding: EdgeInsets.all(24),
                child: Text('Could not load the registration document.'),
              ),
            ),
          ),
        ),
      );
    } on AdminApiError catch (error) {
      if (mounted) _toast(context, error.message, error: true);
    }
  }

  void _details(Map<String, dynamic> car) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => _ApprovalDetailSheet(
        car: car,
        onApprove: () => _approve(car),
        onReject: () => _reject(car),
        onViewRegistration: () => _viewRegistrationDocument(car),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const _AdminLoading();
    if (_error != null) return _AdminError(message: _error!, onRetry: _load);
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text(
            'Vehicle approvals',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 5),
          const Text(
            'Inspect documents and resolve pending vehicle submissions.',
          ),
          const SizedBox(height: 18),
          if (_cars.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 60),
              child: Center(child: Text('No pending vehicle approvals.')),
            ),
          for (final car in _cars)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.directions_car_outlined),
              title: Text(
                '${car['driverName'] ?? 'Driver'} · ${car['make'] ?? ''} ${car['model'] ?? ''}',
              ),
              subtitle: Text(
                '${car['year'] ?? ''} · ${car['availableSeats'] ?? '—'} seats',
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => _details(car),
            ),
        ],
      ),
    );
  }
}

class _ApprovalDetailSheet extends StatelessWidget {
  const _ApprovalDetailSheet({
    required this.car,
    required this.onApprove,
    required this.onReject,
    required this.onViewRegistration,
  });

  final Map<String, dynamic> car;
  final VoidCallback onApprove;
  final VoidCallback onReject;
  final VoidCallback onViewRegistration;

  @override
  Widget build(BuildContext context) {
    final photos = car['photoPaths'] is List
        ? (car['photoPaths'] as List)
        : const [];
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          4,
          20,
          20 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              '${car['make'] ?? ''} ${car['model'] ?? ''}',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const SizedBox(height: 8),
            Text('Driver: ${car['driverName'] ?? '—'}'),
            Text(
              'Year ${car['year'] ?? '—'} · ${car['availableSeats'] ?? '—'} seats',
            ),
            Text('Submitted ${car['createdAt'] ?? '—'}'),
            const Divider(height: 26),
            const Text('Vehicle photos'),
            for (final path in photos)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(8),
                  child: Image.network(
                    path.toString(),
                    height: 150,
                    fit: BoxFit.contain,
                    errorBuilder: (context, error, stackTrace) =>
                        const SizedBox(
                          height: 60,
                          child: Center(
                            child: Text('Could not load vehicle photo.'),
                          ),
                        ),
                  ),
                ),
              ),
            const SizedBox(height: 8),
            Text(
              car['regDocPath']?.toString().startsWith('private://') == true
                  ? 'Registration document: private'
                  : 'Registration document: ${car['regDocPath'] ?? 'Not provided'}',
            ),
            if (car['regDocPath']?.toString().startsWith('private://') == true)
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: onViewRegistration,
                  icon: const Icon(Icons.visibility_outlined),
                  label: const Text('View registration document'),
                ),
              ),
            const SizedBox(height: 10),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: onApprove,
              child: const Text('Approve vehicle'),
            ),
            OutlinedButton(
              onPressed: onReject,
              child: const Text('Reject with reason'),
            ),
          ],
        ),
      ),
    );
  }
}

class AdminUsersScreen extends ConsumerStatefulWidget {
  const AdminUsersScreen({super.key});

  @override
  ConsumerState<AdminUsersScreen> createState() => _AdminUsersScreenState();
}

class _AdminUsersScreenState extends ConsumerState<AdminUsersScreen> {
  final _search = TextEditingController();
  List<Map<String, dynamic>> _users = const [];
  int _page = 1;
  int _totalPages = 1;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load({int page = 1}) async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await ref
          .read(adminApiProvider)
          .get('admin/users', query: {'page': page, 'pageSize': 20});
      final items = data['items'];
      if (!mounted) return;
      setState(() {
        _users = items is List ? items.map(_map).toList() : const [];
        _page = page;
        _totalPages = asInt(data['totalPages']);
        _loading = false;
      });
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  void _showUser(Map<String, dynamic> user) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => _UserDetailSheet(
        user: user,
        onRefresh: () => _load(page: _page),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const _AdminLoading();
    if (_error != null) {
      return _AdminError(
        message: _error!,
        onRetry: () => _load(page: _page),
      );
    }
    final term = _search.text.trim().toLowerCase();
    final visible = _users
        .where(
          (user) => '${user['fullName']} ${user['email']} ${user['role']}'
              .toLowerCase()
              .contains(term),
        )
        .toList();
    return RefreshIndicator(
      onRefresh: () => _load(page: _page),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text('Users', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 6),
          const Text(
            'Search applies to the current API page; server filtering is not available.',
          ),
          const SizedBox(height: 14),
          TextField(
            controller: _search,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'Search current page',
              prefixIcon: Icon(Icons.search),
            ),
          ),
          const SizedBox(height: 12),
          for (final user in visible)
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text((user['fullName'] ?? 'User').toString()),
              subtitle: Text(
                '${user['email'] ?? ''} · ${user['role'] ?? ''} · trust ${user['trustScore'] ?? '—'}',
              ),
              trailing: Text(
                user['isSuspended'] == true ? 'Blocked' : 'Active',
              ),
              onTap: () => _showUser(user),
            ),
          if (visible.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 36),
              child: Center(
                child: Text(
                  _users.isEmpty
                      ? 'No users on this page.'
                      : 'No users match this search on the current page.',
                ),
              ),
            ),
          if (_totalPages > 1)
            Row(
              children: [
                IconButton(
                  tooltip: 'Previous page',
                  onPressed: _page > 1 ? () => _load(page: _page - 1) : null,
                  icon: const Icon(Icons.chevron_left),
                ),
                Text('Page $_page of $_totalPages'),
                IconButton(
                  tooltip: 'Next page',
                  onPressed: _page < _totalPages
                      ? () => _load(page: _page + 1)
                      : null,
                  icon: const Icon(Icons.chevron_right),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

class _UserDetailSheet extends StatefulWidget {
  const _UserDetailSheet({required this.user, required this.onRefresh});

  final Map<String, dynamic> user;
  final Future<void> Function() onRefresh;

  @override
  State<_UserDetailSheet> createState() => _UserDetailSheetState();
}

class _UserDetailSheetState extends State<_UserDetailSheet> {
  Map<String, dynamic>? _detail;
  String? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final api = ProviderScope.containerOf(
        context,
        listen: false,
      ).read(adminApiProvider);
      final detail = await api.get('admin/users/${widget.user['id']}');
      if (mounted) {
        setState(() {
          _detail = detail;
          _loading = false;
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final user = _detail ?? widget.user;
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          4,
          20,
          20 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: _loading
            ? const SizedBox(
                height: 180,
                child: Center(child: CircularProgressIndicator()),
              )
            : ListView(
                shrinkWrap: true,
                children: [
                  Text(
                    (user['fullName'] ?? 'User').toString(),
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const SizedBox(height: 8),
                  Text('${user['email'] ?? ''} · ${user['role'] ?? ''}'),
                  Text(
                    'Trust score ${user['trustScore'] ?? '—'} · email verified ${user['isEmailVerified'] == true ? 'yes' : 'no'}',
                  ),
                  Text('Created ${user['createdAt'] ?? '—'}'),
                  if (_error != null) Text(_error!),
                  if (user['driverProfile'] case final Map profile?) ...[
                    const Divider(height: 28),
                    Text(
                      'Driver profile · rating ${profile['averageRating'] ?? '—'}',
                    ),
                    Text(
                      'Liveness ${profile['livenessVerified'] == true ? 'verified' : 'not verified'} · fails ${profile['livenessFailCount'] ?? 0}',
                    ),
                    for (final rawCar
                        in (profile['cars'] is List
                            ? profile['cars'] as List
                            : const []))
                      ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(
                          '${_map(rawCar)['make']} ${_map(rawCar)['model']}',
                        ),
                        subtitle: Text(
                          'Year ${_map(rawCar)['year']} · ${_map(rawCar)['status']}',
                        ),
                      ),
                  ],
                  const Divider(height: 28),
                  const Text(
                    'Ride, rating, cancellation, and received-flag histories are not returned by this API.',
                  ),
                  const SizedBox(height: 14),
                  if (user['isSuspended'] == true)
                    FilledButton(
                      onPressed: () => _unblock(user['id'].toString()),
                      child: const Text('Unblock account'),
                    )
                  else
                    FilledButton.tonal(
                      onPressed: () => _block(user['id'].toString()),
                      child: const Text('Block account'),
                    ),
                ],
              ),
      ),
    );
  }

  Future<void> _block(String id) async {
    final reason = await _prompt(
      context,
      title: 'Block user',
      label: 'Reason (10–500 characters)',
      minLength: 10,
    );
    if (reason == null || !mounted) return;
    final api = ProviderScope.containerOf(
      context,
      listen: false,
    ).read(adminApiProvider);
    try {
      await api.put('admin/users/$id/block', body: {'reason': reason});
      if (mounted) {
        _toast(context, 'User blocked.');
        Navigator.pop(context);
        await widget.onRefresh();
      }
    } on AdminApiError catch (error) {
      if (mounted) _toast(context, error.message, error: true);
    }
  }

  Future<void> _unblock(String id) async {
    if (!await _confirm(context, 'Unblock this account?')) return;
    if (!mounted) return;
    final api = ProviderScope.containerOf(
      context,
      listen: false,
    ).read(adminApiProvider);
    try {
      await api.put('admin/users/$id/unblock');
      if (mounted) {
        _toast(context, 'User unblocked.');
        Navigator.pop(context);
        await widget.onRefresh();
      }
    } on AdminApiError catch (error) {
      if (mounted) _toast(context, error.message, error: true);
    }
  }
}

class AdminRidesScreen extends ConsumerStatefulWidget {
  const AdminRidesScreen({super.key});

  @override
  ConsumerState<AdminRidesScreen> createState() => _AdminRidesScreenState();
}

class _AdminRidesScreenState extends ConsumerState<AdminRidesScreen> {
  List<Map<String, dynamic>> _rides = const [];
  int _page = 1;
  int _totalPages = 1;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({int page = 1}) async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await ref
          .read(adminApiProvider)
          .get('admin/rides', query: {'page': page, 'pageSize': 20});
      final rides = data['items'];
      if (mounted) {
        setState(() {
          _rides = rides is List ? rides.map(_map).toList() : const [];
          _page = page;
          _totalPages = asInt(data['totalPages']);
          _loading = false;
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  Future<void> _cancel(Map<String, dynamic> ride) async {
    if (!await _confirm(context, 'Cancel ride ${ride['id']} as an Admin?')) {
      return;
    }
    try {
      await ref.read(adminApiProvider).delete('admin/rides/${ride['id']}');
      if (mounted) _toast(context, 'Ride cancelled.');
      await _load(page: _page);
    } on AdminApiError catch (error) {
      if (mounted) _toast(context, error.message, error: true);
    }
  }

  void _detail(Map<String, dynamic> ride) {
    final stops = ride['stops'] is List
        ? (ride['stops'] as List).map(_map).toList()
        : const <Map<String, dynamic>>[];
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                'Ride detail',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              Text('${ride['driverName'] ?? 'Driver'}'),
              _AdminLifecycleSummary(
                status: '${ride['status'] ?? 'Unknown'}',
                kind: _AdminLifecycleKind.ride,
              ),
              Text(
                'Fare ${ride['fare'] ?? '—'} · max seats ${ride['maxSeats'] ?? '—'}',
              ),
              const Divider(height: 25),
              for (final stop in stops)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: Text('${stop['sequenceOrder'] ?? ''}'),
                  title: Text('${stop['name'] ?? ''}'),
                  subtitle: Text('${stop['status'] ?? 'Upcoming'}'),
                ),
              if ('${ride['status']}'.toLowerCase() == 'active' ||
                  '${ride['status']}'.toLowerCase() == 'posted')
                FilledButton.tonal(
                  onPressed: () => _cancel(ride),
                  child: const Text('Cancel ride'),
                ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const _AdminLoading();
    if (_error != null) {
      return _AdminError(
        message: _error!,
        onRetry: () => _load(page: _page),
      );
    }
    return RefreshIndicator(
      onRefresh: () => _load(page: _page),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text('Rides', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 5),
          const Text(
            'Status filters are not exposed by the current API; review the paginated feed.',
          ),
          const SizedBox(height: 12),
          for (final ride in _rides)
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text('${ride['driverName'] ?? 'Driver'}'),
              subtitle: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${ride['carMake'] ?? ''} ${ride['carModel'] ?? ''} · fare ${ride['fare'] ?? '—'}',
                  ),
                  _AdminLifecycleSummary(
                    status: '${ride['status'] ?? 'Unknown'}',
                    kind: _AdminLifecycleKind.ride,
                  ),
                ],
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => _detail(ride),
            ),
          if (_rides.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 44),
              child: Center(child: Text('No rides to review.')),
            ),
          if (_totalPages > 1)
            Row(
              children: [
                IconButton(
                  onPressed: _page > 1 ? () => _load(page: _page - 1) : null,
                  icon: const Icon(Icons.chevron_left),
                ),
                Text('Page $_page of $_totalPages'),
                IconButton(
                  onPressed: _page < _totalPages
                      ? () => _load(page: _page + 1)
                      : null,
                  icon: const Icon(Icons.chevron_right),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

class AdminFlagsScreen extends ConsumerStatefulWidget {
  const AdminFlagsScreen({super.key});

  @override
  ConsumerState<AdminFlagsScreen> createState() => _AdminFlagsScreenState();
}

class _AdminFlagsScreenState extends ConsumerState<AdminFlagsScreen> {
  List<Map<String, dynamic>> _flags = const [];
  int _page = 1;
  int _totalPages = 1;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({int page = 1}) async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await ref
          .read(adminApiProvider)
          .get('admin/flags', query: {'page': page, 'pageSize': 20});
      final flags = data['items'];
      if (mounted) {
        setState(() {
          _flags = flags is List ? flags.map(_map).toList() : const [];
          _page = page;
          _totalPages = asInt(data['totalPages']);
          _loading = false;
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  Future<void> _dismiss(Map<String, dynamic> flag) async {
    final notes = await _prompt(
      context,
      title: 'Dismiss flag',
      label: 'Notes (optional)',
      maxLength: 500,
    );
    if (notes == null || !mounted) return;
    try {
      await ref
          .read(adminApiProvider)
          .put('admin/flags/${flag['id']}/dismiss', body: {'notes': notes});
      if (mounted) _toast(context, 'Flag dismissed.');
      await _load(page: _page);
    } on AdminApiError catch (error) {
      if (mounted) _toast(context, error.message, error: true);
    }
  }

  Future<void> _act(Map<String, dynamic> flag) async {
    final action = await _prompt(
      context,
      title: 'Take action on flag',
      label: 'Action text. Include “warn” or “block” as intended (10–500 characters).',
      minLength: 10,
    );
    if (action == null || !mounted) return;
    try {
      await ref
          .read(adminApiProvider)
          .put('admin/flags/${flag['id']}/action', body: {'action': action});
      if (mounted) _toast(context, 'Flag action recorded.');
      await _load(page: _page);
    } on AdminApiError catch (error) {
      if (mounted) _toast(context, error.message, error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const _AdminLoading();
    if (_error != null) {
      return _AdminError(
        message: _error!,
        onRetry: () => _load(page: _page),
      );
    }
    return RefreshIndicator(
      onRefresh: () => _load(page: _page),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
        children: [
          Text('Flags', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 6),
          const Text('Review reports, then dismiss or record an action.'),
          const SizedBox(height: 12),
          for (final flag in _flags)
            ExpansionTile(
              tilePadding: EdgeInsets.zero,
              title: Text(
                '${flag['reporterName'] ?? 'Reporter'} → ${flag['reporteeName'] ?? 'User'}',
              ),
              subtitle: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Ride ${flag['rideId'] ?? ''}'),
                  _AdminLifecycleSummary(
                    status: '${flag['status'] ?? 'Unknown'}',
                    kind: _AdminLifecycleKind.flag,
                  ),
                ],
              ),
              children: [
                Align(
                  alignment: Alignment.centerLeft,
                  child: Text('${flag['reason'] ?? ''}'),
                ),
                if ('${flag['status']}'.toLowerCase() == 'open')
                  Wrap(
                    spacing: 8,
                    children: [
                      TextButton(
                        onPressed: () => _dismiss(flag),
                        child: const Text('Dismiss'),
                      ),
                      FilledButton.tonal(
                        onPressed: () => _act(flag),
                        child: const Text('Take action'),
                      ),
                    ],
                  )
                else
                  const Padding(
                    padding: EdgeInsets.symmetric(vertical: 8),
                    child: Text(
                      'This flag is already resolved; no further action is available.',
                    ),
                  ),
              ],
            ),
          if (_flags.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 44),
              child: Center(child: Text('No flags to review.')),
            ),
          if (_totalPages > 1)
            Row(
              children: [
                IconButton(
                  onPressed: _page > 1 ? () => _load(page: _page - 1) : null,
                  icon: const Icon(Icons.chevron_left),
                ),
                Text('Page $_page of $_totalPages'),
                IconButton(
                  onPressed: _page < _totalPages
                      ? () => _load(page: _page + 1)
                      : null,
                  icon: const Icon(Icons.chevron_right),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

class AdminReportsScreen extends ConsumerStatefulWidget {
  const AdminReportsScreen({super.key});

  @override
  ConsumerState<AdminReportsScreen> createState() => _AdminReportsScreenState();
}

class _AdminReportsScreenState extends ConsumerState<AdminReportsScreen> {
  DateTimeRange? _range;
  Map<String, dynamic>? _report;
  bool _loading = false;
  bool _exporting = false;
  String? _error;

  Future<void> _chooseRange() async {
    final now = DateTime.now();
    final result = await showDateRangePicker(
      context: context,
      firstDate: DateTime(2020),
      lastDate: now,
      initialDateRange:
          _range ??
          DateTimeRange(
            start: now.subtract(const Duration(days: 30)),
            end: now,
          ),
    );
    if (result != null && mounted) setState(() => _range = result);
  }

  Map<String, dynamic> _query() {
    final range = _range;
    if (range == null) return const {};
    return {
      'from': range.start.toUtc().toIso8601String(),
      'to': range.end.toUtc().toIso8601String(),
    };
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final report = await ref
          .read(adminApiProvider)
          .get('admin/reports', query: _query());
      if (mounted) {
        setState(() {
          _report = report;
          _loading = false;
        });
      }
    } on AdminApiError catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _loading = false;
        });
      }
    }
  }

  Future<void> _export(String format) async {
    setState(() {
      _exporting = true;
      _error = null;
    });
    try {
      final bytes = await ref
          .read(adminApiProvider)
          .download(
            'admin/reports/export',
            query: {..._query(), 'format': format},
          );
      final directory = await getApplicationDocumentsDirectory();
      final filename =
          'pakicarpool-report-${DateTime.now().toUtc().millisecondsSinceEpoch}.$format';
      final file = File('${directory.path}/$filename');
      await file.writeAsBytes(bytes, flush: true);
      if (mounted) _toast(context, 'Report saved to ${file.path}');
    } on AdminApiError catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _exporting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final report = _report ?? const <String, dynamic>{};
    final summary = _map(report['summary']);
    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
      children: [
        Text('Reports', style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 5),
        const Text(
          'The current report endpoint returns a platform summary and ride sample.',
        ),
        const SizedBox(height: 16),
        OutlinedButton.icon(
          onPressed: _chooseRange,
          icon: const Icon(Icons.date_range),
          label: Text(
            _range == null
                ? 'Choose date range'
                : '${_date(_range!.start)} – ${_date(_range!.end)}',
          ),
        ),
        const SizedBox(height: 8),
        FilledButton(
          onPressed: _loading ? null : _load,
          child: _loading
              ? const CircularProgressIndicator()
              : const Text('Generate report'),
        ),
        if (_error != null) ...[
          const SizedBox(height: 12),
          _AdminNotice(message: _error!, error: true),
        ],
        if (_report != null) ...[
          const Divider(height: 28),
          _metric('Rides in selected period', report['ridesInRange']),
          _metric('Completed in period', report['completedInRange']),
          _metric('Cancelled in period', report['cancelledInRange']),
          _metric('Total rides sampled', report['totalRidesSampled']),
          const Divider(height: 24),
          _metric('Total users', summary['totalUsers']),
          _metric('Active rides', summary['activeRides']),
          _metric('Pending approvals', summary['pendingCarApprovals']),
          _metric('Open flags', summary['openFlags']),
        ],
        const Divider(height: 26),
        const Text(
          'Bookings, ratings overview, user-type filters, and ride-status filters require expanded report API data.',
        ),
        const SizedBox(height: 12),
        Wrap(
          spacing: 8,
          children: [
            OutlinedButton.icon(
              onPressed: _exporting ? null : () => _export('csv'),
              icon: const Icon(Icons.table_view_outlined),
              label: const Text('Export CSV'),
            ),
            OutlinedButton.icon(
              onPressed: _exporting ? null : () => _export('pdf'),
              icon: const Icon(Icons.picture_as_pdf_outlined),
              label: const Text('Export PDF'),
            ),
          ],
        ),
        if (_exporting) const LinearProgressIndicator(),
      ],
    );
  }

  Widget _metric(String label, Object? value) => ListTile(
    contentPadding: EdgeInsets.zero,
    title: Text(label),
    trailing: Text(
      '${value ?? '—'}',
      style: Theme.of(context).textTheme.titleMedium,
    ),
  );
}

class _MetricLine extends StatelessWidget {
  const _MetricLine({
    required this.label,
    required this.value,
    required this.icon,
  });

  final String label;
  final Object? value;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 10),
    child: Row(
      children: [
        Icon(icon, size: 20, color: AdminColors.teal),
        const SizedBox(width: 9),
        Expanded(
          child: Text(label, style: Theme.of(context).textTheme.bodySmall),
        ),
        Text('${value ?? '—'}', style: Theme.of(context).textTheme.titleLarge),
      ],
    ),
  );
}

enum _AdminLifecycleKind { ride, flag }

class _AdminLifecycleSummary extends StatelessWidget {
  const _AdminLifecycleSummary({required this.status, required this.kind});

  final String status;
  final _AdminLifecycleKind kind;

  @override
  Widget build(BuildContext context) {
    final normalized = status.toLowerCase().replaceAll('_', '');
    final (description, color) = switch ((kind, normalized)) {
      (_AdminLifecycleKind.ride, 'posted') => (
        'Open for booking.',
        AdminColors.teal,
      ),
      (_AdminLifecycleKind.ride, 'active') => (
        'The ride is underway.',
        AdminColors.teal,
      ),
      (_AdminLifecycleKind.ride, 'completed') => (
        'This ride has finished.',
        AdminColors.muted,
      ),
      (_AdminLifecycleKind.ride, 'cancelled') => (
        'This ride was cancelled.',
        AdminColors.coral,
      ),
      (_AdminLifecycleKind.flag, 'open') => (
        'Awaiting admin review.',
        AdminColors.coral,
      ),
      (_AdminLifecycleKind.flag, 'dismissed') => (
        'Reviewed; no action was taken.',
        AdminColors.muted,
      ),
      (_AdminLifecycleKind.flag, 'actiontaken') => (
        'An action was recorded.',
        AdminColors.teal,
      ),
      _ => ('Status: $status', AdminColors.muted),
    };
    return Padding(
      padding: const EdgeInsets.only(top: 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Chip(
            label: Text(status),
            visualDensity: VisualDensity.compact,
            side: BorderSide(color: color.withValues(alpha: 0.35)),
            backgroundColor: color.withValues(alpha: 0.08),
            labelStyle: TextStyle(color: color, fontWeight: FontWeight.w600),
          ),
          Text(
            description,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: color),
          ),
        ],
      ),
    );
  }
}

class _AdminWordmark extends StatelessWidget {
  const _AdminWordmark();

  @override
  Widget build(BuildContext context) => Row(
    children: [
      const Icon(Icons.alt_route, color: AdminColors.teal, size: 34),
      const SizedBox(width: 9),
      Text(
        'PakiCarpool',
        style: Theme.of(context).textTheme.titleLarge
            ?.copyWith(fontWeight: FontWeight.w700),
      ),
      const SizedBox(width: 9),
      const Text(
        'OPERATIONS',
        style: TextStyle(
          fontSize: 11,
          color: AdminColors.muted,
          fontWeight: FontWeight.w700,
        ),
      ),
    ],
  );
}

class _AdminLoading extends StatelessWidget {
  const _AdminLoading();

  @override
  Widget build(BuildContext context) =>
      const Center(child: CircularProgressIndicator());
}

class _AdminError extends StatelessWidget {
  const _AdminError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(message, textAlign: TextAlign.center),
          const SizedBox(height: 14),
          OutlinedButton(onPressed: onRetry, child: const Text('Try again')),
        ],
      ),
    ),
  );
}

class _AdminNotice extends StatelessWidget {
  const _AdminNotice({required this.message, required this.error});

  final String message;
  final bool error;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: error ? const Color(0xFFF8E9E7) : const Color(0xFFE1F2EF),
      borderRadius: BorderRadius.circular(8),
    ),
    child: Text(
      message,
      style: TextStyle(color: error ? AdminColors.coral : AdminColors.ink),
    ),
  );
}

Future<String?> _prompt(
  BuildContext context, {
  required String title,
  required String label,
  int minLength = 1,
  int maxLength = 500,
}) async {
  final controller = TextEditingController();
  String? error;
  final result = await showDialog<String>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, setState) => AlertDialog(
        title: Text(title),
        content: TextField(
          controller: controller,
          minLines: 3,
          maxLines: 6,
          maxLength: maxLength,
          decoration: InputDecoration(labelText: label, errorText: error),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              if (controller.text.trim().length < minLength) {
                setState(() => error = 'Enter at least $minLength characters.');
                return;
              }
              Navigator.pop(context, controller.text.trim());
            },
            child: const Text('Submit'),
          ),
        ],
      ),
    ),
  );
  controller.dispose();
  return result;
}

Future<bool> _confirm(BuildContext context, String message) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        content: Text(message),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Confirm'),
          ),
        ],
      ),
    ) ??
    false;

void _toast(BuildContext context, String message, {bool error = false}) {
  ScaffoldMessenger.of(context).showSnackBar(
    SnackBar(
      content: Text(message),
      backgroundColor: error ? AdminColors.coral : AdminColors.ink,
    ),
  );
}

Map<String, dynamic> _map(Object? value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) return value.map((key, item) => MapEntry('$key', item));
  return const {};
}

int asInt(Object? value) => value is int ? value : int.tryParse('$value') ?? 0;

String _date(DateTime date) =>
    '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

String _dateTime(Object? value) {
  final date = DateTime.tryParse(value?.toString() ?? '')?.toLocal();
  if (date == null) return 'Just now';
  return '${_date(date)} ${date.hour.toString().padLeft(2, '0')}:${date.minute.toString().padLeft(2, '0')}';
}
