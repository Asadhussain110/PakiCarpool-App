import 'dart:async';
import 'dart:io';

import 'package:flutter/cupertino.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../../../core/network/api_client.dart';
import '../../../core/realtime/signalr_service.dart';
import '../../../core/theme/route_atlas_theme.dart';
import '../data/auth_repository.dart';
import '../models/app_session.dart';
import '../models/auth_session.dart';
import 'captcha_challenge.dart';

const hCaptchaSiteKey = String.fromEnvironment(
  'HCAPTCHA_SITE_KEY',
  defaultValue: localBuild ? localHCaptchaSiteKey : '',
);

class WelcomeScreen extends StatelessWidget {
  const WelcomeScreen({super.key});

  @override
  Widget build(BuildContext context) => _page(
    context,
    SafeArea(
      child: LayoutBuilder(
        builder: (context, constraints) => SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(24, 28, 24, 30),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: ConstrainedBox(
                constraints: BoxConstraints(
                  minHeight: constraints.maxHeight - 58,
                ),
                child: IntrinsicHeight(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const _BrandWordmark(),
                      const SizedBox(height: 54),
                      const CustomPaint(
                        size: Size(double.infinity, 190),
                        painter: _RouteAtlasPainter(),
                      ),
                      const SizedBox(height: 28),
                      Text(
                        'Share the way.',
                        style: GoogleFonts.barlow(
                          fontSize: 42,
                          height: 1.0,
                          fontWeight: FontWeight.w700,
                          color: RouteAtlasColors.ink,
                        ),
                      ),
                      const SizedBox(height: 10),
                      const Text(
                        'Find a seat along your route, or share the spare ones in your car.',
                        style: TextStyle(
                          fontSize: 17,
                          height: 1.45,
                          color: RouteAtlasColors.muted,
                        ),
                      ),
                      const SizedBox(height: 28),
                      FilledButton.icon(
                        onPressed: () =>
                            context.push('/register?role=Passenger'),
                        icon: const Icon(Icons.person_outline),
                        label: const Text('I need a ride'),
                      ),
                      const SizedBox(height: 10),
                      OutlinedButton.icon(
                        onPressed: () => context.push('/register?role=Driver'),
                        icon: const Icon(Icons.directions_car_outlined),
                        label: const Text('I have seats to share'),
                      ),
                      const SizedBox(height: 18),
                      TextButton(
                        onPressed: () => context.go('/login'),
                        child: const Text('Already have an account? Sign in'),
                      ),
                      const Spacer(),
                      const Text(
                        'Rides follow real driver-posted stops. Guests need an account to browse and book.',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          color: RouteAtlasColors.secondaryMuted,
                          height: 1.4,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

class _RouteAtlasPainter extends CustomPainter {
  const _RouteAtlasPainter();

  @override
  void paint(Canvas canvas, Size size) {
    final route = Paint()
      ..color = RouteAtlasColors.teal
      ..strokeWidth = 4
      ..strokeCap = StrokeCap.round
      ..style = PaintingStyle.stroke;
    final path = Path()
      ..moveTo(size.width * .08, size.height * .78)
      ..cubicTo(
        size.width * .22,
        size.height * .10,
        size.width * .48,
        size.height * .9,
        size.width * .63,
        size.height * .40,
      )
      ..cubicTo(
        size.width * .74,
        size.height * .03,
        size.width * .83,
        size.height * .88,
        size.width * .94,
        size.height * .24,
      );
    canvas.drawPath(path, route);
    final stops = [
      Offset(size.width * .08, size.height * .78),
      Offset(size.width * .37, size.height * .47),
      Offset(size.width * .63, size.height * .40),
      Offset(size.width * .94, size.height * .24),
    ];
    for (var index = 0; index < stops.length; index++) {
      canvas.drawCircle(
        stops[index],
        15,
        Paint()
          ..color = index == 0
              ? RouteAtlasColors.saffron
              : RouteAtlasColors.teal,
      );
      canvas.drawCircle(
        stops[index],
        15,
        Paint()
          ..color = RouteAtlasColors.white
          ..style = PaintingStyle.stroke
          ..strokeWidth = 3,
      );
      final painter = TextPainter(
        text: TextSpan(
          text: '${index + 1}',
          style: const TextStyle(
            color: RouteAtlasColors.ink,
            fontSize: 12,
            fontWeight: FontWeight.w700,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      painter.paint(
        canvas,
        stops[index] - Offset(painter.width / 2, painter.height / 2),
      );
    }
    final labels = ['North', 'Link', 'Market', 'Coast'];
    for (var index = 0; index < stops.length; index++) {
      final painter = TextPainter(
        text: TextSpan(
          text: labels[index],
          style: const TextStyle(
            color: RouteAtlasColors.muted,
            fontSize: 12,
            fontWeight: FontWeight.w600,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      painter.paint(
        canvas,
        Offset(stops[index].dx - painter.width / 2, stops[index].dy + 21),
      );
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class ResetPasswordScreen extends ConsumerStatefulWidget {
  const ResetPasswordScreen({required this.email, super.key});

  final String email;

  @override
  ConsumerState<ResetPasswordScreen> createState() =>
      _ResetPasswordScreenState();
}

class _ResetPasswordScreenState extends ConsumerState<ResetPasswordScreen> {
  final _password = TextEditingController();
  final _confirm = TextEditingController();
  final _code = TextEditingController();
  bool _submitting = false;
  String? _error;
  String? _notice;

  @override
  void dispose() {
    _password.dispose();
    _confirm.dispose();
    _code.dispose();
    super.dispose();
  }

  String? _validatePassword(String? value) {
    final password = value ?? '';
    if (password.length < 8 || password.length > 128) {
      return 'Use 8 to 128 characters.';
    }
    if (!RegExp(r'[a-z]').hasMatch(password) ||
        !RegExp(r'[A-Z]').hasMatch(password) ||
        !RegExp(r'\d').hasMatch(password) ||
        !RegExp(r'[^A-Za-z0-9]').hasMatch(password)) {
      return 'Add uppercase, lowercase, a number, and a symbol.';
    }
    return null;
  }

  Future<void> _submit() async {
    if (widget.email.isEmpty || !RegExp(r'^\d{6}$').hasMatch(_code.text)) {
      setState(() => _error = 'Enter the 6-digit code from your email.');
      return;
    }
    final passwordError = _validatePassword(_password.text);
    if (passwordError != null) {
      setState(() => _error = passwordError);
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
          .read(authRepositoryProvider)
          .resetPassword(
            email: widget.email,
            code: _code.text,
            newPassword: _password.text,
          );
      if (mounted) {
        setState(
          () => _notice = 'Password reset. Sign in with your new password.',
        );
      }
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _error = failure.message);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => _page(
    context,
    SafeArea(
      child: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Text(
            'Choose a new password',
            style: GoogleFonts.barlow(
              fontSize: 32,
              fontWeight: FontWeight.w600,
              color: RouteAtlasColors.ink,
            ),
          ),
          const SizedBox(height: 8),
          Text('Enter the code sent to ${widget.email}.'),
          const SizedBox(height: 22),
          TextField(
            controller: _code,
            keyboardType: TextInputType.number,
            maxLength: 6,
            inputFormatters: [FilteringTextInputFormatter.digitsOnly],
            decoration: const InputDecoration(labelText: 'Email code'),
          ),
          const SizedBox(height: 14),
          TextField(
            controller: _password,
            obscureText: true,
            decoration: const InputDecoration(labelText: 'New password'),
          ),
          const SizedBox(height: 14),
          TextField(
            controller: _confirm,
            obscureText: true,
            decoration: const InputDecoration(
              labelText: 'Confirm new password',
            ),
          ),
          const SizedBox(height: 8),
          const Text('Use uppercase, lowercase, a number, and a symbol.'),
          if (_error != null) ...[
            const SizedBox(height: 14),
            _MessageBanner(message: _error!, isError: true),
          ],
          if (_notice != null) ...[
            const SizedBox(height: 14),
            _MessageBanner(message: _notice!, isError: false),
          ],
          const SizedBox(height: 22),
          FilledButton(
            onPressed: _submitting ? null : _submit,
            child: _submitting
                ? const CircularProgressIndicator()
                : const Text('Reset password'),
          ),
          TextButton(
            onPressed: () => context.go('/login'),
            child: const Text('Return to sign in'),
          ),
        ],
      ),
    ),
  );
}

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({
    this.verifiedEmail = false,
    this.sessionExpired = false,
    super.key,
  });

  final bool verifiedEmail;
  final bool sessionExpired;

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  bool _obscurePassword = true;
  bool _submitting = false;
  bool _captchaRequired = false;
  String? _captchaToken;
  String? _errorMessage;
  String? _noticeMessage;
  final _serverAddressController = TextEditingController(text: apiBaseUrl);

  @override
  void initState() {
    super.initState();
    if (widget.sessionExpired) {
      _noticeMessage = 'Your session has expired. Please log in again.';
    } else if (widget.verifiedEmail) {
      _noticeMessage = 'Email verified. You can sign in now.';
    }
  }

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    _serverAddressController.dispose();
    super.dispose();
  }

  Future<void> _saveServerAddress() async {
    try {
      final address = normalizeApiBaseUrl(_serverAddressController.text);
      await ref
          .read(secureStorageProvider)
          .write(key: apiBaseUrlStorageKey, value: address);
      ref.read(apiClientProvider).setBaseUrl(address);
      if (mounted) {
        setState(() {
          _serverAddressController.text = address;
          _noticeMessage = 'Server address saved.';
          _errorMessage = null;
        });
      }
    } on FormatException catch (error) {
      if (mounted) {
        setState(() {
          _errorMessage = error.message;
          _noticeMessage = null;
        });
      }
    } on Exception catch (_) {
      if (mounted) {
        setState(() {
          _errorMessage = 'Server address could not be saved.';
          _noticeMessage = null;
        });
      }
    }
  }

  Future<void> _signIn() async {
    FocusManager.instance.primaryFocus?.unfocus();
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_captchaRequired && _captchaToken == null) {
      setState(
        () => _errorMessage = 'Complete the security check to continue.',
      );
      return;
    }

    setState(() {
      _submitting = true;
      _errorMessage = null;
      _noticeMessage = null;
    });

    try {
      final session = await ref
          .read(authRepositoryProvider)
          .signIn(
            email: _emailController.text.trim(),
            password: _passwordController.text,
            hCaptchaToken: _captchaToken,
          );
      if (!mounted) return;
      ref.read(appSessionProvider.notifier).set(session);
      unawaited(ref.read(signalRServiceProvider).connect().catchError((_) {}));
      _captchaRequired = false;
      _captchaToken = null;
      context.go(
        session.role == PakiRole.driver && session.livenessPending
            ? '/driver/liveness'
            : '/home',
      );
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _errorMessage = _friendlyError(failure);
        if (failure.code == 'CAPTCHA_REQUIRED') {
          _captchaRequired = true;
          _captchaToken = null;
        }
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _errorMessage = 'Sign-in failed. Check your connection and try again.';
      });
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  String _friendlyError(ApiFailure failure) {
    if (failure.code == 'ACCOUNT_LOCKED') {
      return 'Too many attempts. Wait 15 minutes before trying again.';
    }
    if (failure.code == 'ACCOUNT_SUSPENDED') {
      return 'This account is suspended. Contact PakiCarpool support.';
    }
    if (failure.code == 'CAPTCHA_INVALID') {
      return 'Security check expired. Complete it again to continue.';
    }
    return failure.message;
  }

  Future<void> _openRegistration() async {
    final role = await _chooseRole();
    if (role == null || !mounted) return;
    context.push('/register?role=${role.apiValue}');
  }

  Future<PakiRole?> _chooseRole() async {
    if (Platform.isIOS) {
      return showCupertinoModalPopup<PakiRole>(
        context: context,
        builder: (context) => CupertinoActionSheet(
          title: const Text('Create an account'),
          message: const Text('Choose your permanent account role.'),
          actions: [
            CupertinoActionSheetAction(
              onPressed: () => Navigator.pop(context, PakiRole.passenger),
              child: const Text('Passenger'),
            ),
            CupertinoActionSheetAction(
              onPressed: () => Navigator.pop(context, PakiRole.driver),
              child: const Text('Driver'),
            ),
          ],
          cancelButton: CupertinoActionSheetAction(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
        ),
      );
    }
    return showModalBottomSheet<PakiRole>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.person_outline),
              title: const Text('Passenger'),
              onTap: () => Navigator.pop(context, PakiRole.passenger),
            ),
            ListTile(
              leading: const Icon(Icons.directions_car_outlined),
              title: const Text('Driver'),
              onTap: () => Navigator.pop(context, PakiRole.driver),
            ),
            const SizedBox(height: 12),
          ],
        ),
      ),
    );
  }

  Future<void> _googleSignIn() async {
    if (localBuild) return;
    final role = await _chooseRole();
    if (role == null || !mounted) return;
    setState(() {
      _submitting = true;
      _errorMessage = null;
    });
    try {
      final authorizationUrl = await ref
          .read(authRepositoryProvider)
          .googleAuthorizationUrl(role: role);
      if (!mounted) return;
      final session = await context.push<AuthSession>(
        '/google-auth',
        extra: authorizationUrl,
      );
      if (!mounted || session == null) return;
      ref.read(appSessionProvider.notifier).set(session);
      unawaited(ref.read(signalRServiceProvider).connect().catchError((_) {}));
      context.go(
        session.role == PakiRole.driver && session.livenessPending
            ? '/driver/liveness'
            : '/home',
      );
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _errorMessage = failure.message);
    } catch (_) {
      if (mounted) {
        setState(
          () => _errorMessage = 'Google sign-in could not be completed.',
        );
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final content = SafeArea(
      child: LayoutBuilder(
        builder: (context, constraints) => SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 32),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 440),
              child: Form(
                key: _formKey,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const _BrandWordmark(),
                    const SizedBox(height: 42),
                    Text(
                      'Sign in',
                      style: GoogleFonts.barlow(
                        color: RouteAtlasColors.ink,
                        fontSize: 34,
                        fontWeight: FontWeight.w600,
                        height: 1.05,
                      ),
                    ),
                    const SizedBox(height: 10),
                    const Text(
                      'Pick up where your shared route left off.',
                      style: TextStyle(
                        color: RouteAtlasColors.muted,
                        fontSize: 16,
                        height: 1.4,
                      ),
                    ),
                    const SizedBox(height: 30),
                    if (localBuild) ...[
                      _fieldLabel('Server address'),
                      TextFormField(
                        controller: _serverAddressController,
                        keyboardType: TextInputType.url,
                        autocorrect: false,
                        decoration: const InputDecoration(
                          hintText: 'http://192.168.1.10:58240/api/v1',
                        ),
                      ),
                      Align(
                        alignment: Alignment.centerRight,
                        child: TextButton(
                          onPressed: _submitting ? null : _saveServerAddress,
                          child: const Text('Save server address'),
                        ),
                      ),
                      const SizedBox(height: 8),
                    ],
                    _fieldLabel('Email'),
                    _emailField(),
                    const SizedBox(height: 18),
                    _fieldLabel('Password'),
                    _passwordField(),
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton(
                        onPressed: () => context.push('/forgot-password'),
                        child: const Text('Forgot password?'),
                      ),
                    ),
                    if (_captchaRequired) ...[
                      const SizedBox(height: 8),
                      HCaptchaChallenge(
                        siteKey: hCaptchaSiteKey,
                        onToken: (token) => setState(() {
                          _captchaToken = token;
                          _errorMessage = null;
                        }),
                      ),
                    ],
                    if (_errorMessage != null) ...[
                      const SizedBox(height: 12),
                      _MessageBanner(message: _errorMessage!, isError: true),
                    ],
                    if (_noticeMessage != null) ...[
                      const SizedBox(height: 12),
                      _MessageBanner(message: _noticeMessage!, isError: false),
                    ],
                    const SizedBox(height: 14),
                    _primaryButton(),
                    if (!localBuild) ...[
                      const SizedBox(height: 22),
                      const _AlternativeDivider(),
                      const SizedBox(height: 18),
                      _googleButton(),
                    ],
                    const SizedBox(height: 28),
                    Wrap(
                      alignment: WrapAlignment.center,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      spacing: 4,
                      children: [
                        const Text(
                          'New to PakiCarpool?',
                          style: TextStyle(color: RouteAtlasColors.muted),
                        ),
                        TextButton(
                          onPressed: _openRegistration,
                          child: const Text('Create account'),
                        ),
                      ],
                    ),
                    SizedBox(height: constraints.maxHeight < 700 ? 4 : 16),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );

    if (Platform.isIOS) {
      return CupertinoPageScaffold(
        backgroundColor: RouteAtlasColors.canvas,
        child: content,
      );
    }
    return Scaffold(backgroundColor: RouteAtlasColors.canvas, body: content);
  }

  Widget _fieldLabel(String label) => Padding(
    padding: const EdgeInsets.only(bottom: 7),
    child: Text(
      label,
      style: const TextStyle(
        color: RouteAtlasColors.ink,
        fontWeight: FontWeight.w600,
      ),
    ),
  );

  Widget _emailField() {
    String? validator(String? value) {
      final email = value?.trim() ?? '';
      return email.isNotEmpty && email.contains('@')
          ? null
          : 'Enter a valid email address.';
    }

    if (Platform.isIOS) {
      return CupertinoTextFormFieldRow(
        controller: _emailController,
        placeholder: 'you@example.com',
        keyboardType: TextInputType.emailAddress,
        textInputAction: TextInputAction.next,
        autocorrect: false,
        autofillHints: const [AutofillHints.email],
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 16),
        decoration: BoxDecoration(
          color: RouteAtlasColors.white,
          border: Border.all(color: RouteAtlasColors.outline),
          borderRadius: BorderRadius.circular(8),
        ),
        validator: validator,
      );
    }
    return TextFormField(
      controller: _emailController,
      keyboardType: TextInputType.emailAddress,
      textInputAction: TextInputAction.next,
      autofillHints: const [AutofillHints.email],
      autocorrect: false,
      validator: validator,
      decoration: const InputDecoration(hintText: 'you@example.com'),
    );
  }

  Widget _passwordField() {
    String? validator(String? value) =>
        value == null || value.isEmpty ? 'Enter your password.' : null;
    if (Platform.isIOS) {
      return CupertinoTextFormFieldRow(
        controller: _passwordController,
        placeholder: 'Enter your password',
        obscureText: _obscurePassword,
        textInputAction: TextInputAction.done,
        autofillHints: const [AutofillHints.password],
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 16),
        decoration: BoxDecoration(
          color: RouteAtlasColors.white,
          border: Border.all(color: RouteAtlasColors.outline),
          borderRadius: BorderRadius.circular(8),
        ),
        validator: validator,
      );
    }
    return TextFormField(
      controller: _passwordController,
      obscureText: _obscurePassword,
      textInputAction: TextInputAction.done,
      autofillHints: const [AutofillHints.password],
      validator: validator,
      onFieldSubmitted: (_) => _signIn(),
      decoration: InputDecoration(
        hintText: 'Enter your password',
        suffixIcon: IconButton(
          tooltip: _obscurePassword ? 'Show password' : 'Hide password',
          onPressed: () => setState(() => _obscurePassword = !_obscurePassword),
          icon: Icon(
            _obscurePassword
                ? Icons.visibility_outlined
                : Icons.visibility_off_outlined,
          ),
        ),
      ),
    );
  }

  Widget _primaryButton() {
    final enabled =
        !_submitting && (!_captchaRequired || _captchaToken != null);
    if (Platform.isIOS) {
      return SizedBox(
        width: double.infinity,
        child: CupertinoButton.filled(
          onPressed: enabled ? _signIn : null,
          borderRadius: BorderRadius.circular(12),
          child: _submitting
              ? const CupertinoActivityIndicator(color: RouteAtlasColors.white)
              : const Text('Continue'),
        ),
      );
    }
    return FilledButton(
      onPressed: enabled ? _signIn : null,
      child: _submitting
          ? const SizedBox(
              width: 20,
              height: 20,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : const Text('Continue'),
    );
  }

  Widget _googleButton() {
    if (localBuild) return const SizedBox.shrink();
    if (Platform.isIOS) {
      return SizedBox(
        width: double.infinity,
        child: CupertinoButton(
          onPressed: _submitting ? null : _googleSignIn,
          padding: const EdgeInsets.symmetric(vertical: 16),
          child: const Text('Continue with Google'),
        ),
      );
    }
    return OutlinedButton(
      onPressed: _submitting ? null : _googleSignIn,
      child: const Text('Continue with Google'),
    );
  }
}

class RegistrationScreen extends ConsumerStatefulWidget {
  const RegistrationScreen({required this.role, super.key});

  final String role;

  @override
  ConsumerState<RegistrationScreen> createState() => _RegistrationScreenState();
}

class _RegistrationScreenState extends ConsumerState<RegistrationScreen> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _confirmPassword = TextEditingController();
  bool _submitting = false;
  String? _errorMessage;

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    _confirmPassword.dispose();
    super.dispose();
  }

  Future<void> _register() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() {
      _submitting = true;
      _errorMessage = null;
    });
    try {
      final role = widget.role.toLowerCase() == 'driver'
          ? PakiRole.driver
          : PakiRole.passenger;
      await ref
          .read(authRepositoryProvider)
          .register(
            fullName: _name.text.trim(),
            email: _email.text.trim(),
            password: _password.text,
            role: role,
          );
      if (mounted) {
        context.go(
          '/verify-email?email=${Uri.encodeQueryComponent(_email.text.trim())}',
        );
      }
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _errorMessage = failure.message);
    } catch (_) {
      if (mounted) {
        setState(() => _errorMessage = 'Registration failed. Try again.');
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => _page(
    context,
    SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 440),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  TextButton.icon(
                    onPressed: () => context.pop(),
                    icon: const Icon(Icons.arrow_back),
                    label: const Text('Back to sign in'),
                    style: TextButton.styleFrom(
                      alignment: Alignment.centerLeft,
                    ),
                  ),
                  const SizedBox(height: 18),
                  Text(
                    'Create your ${widget.role.toLowerCase()} account',
                    style: GoogleFonts.barlow(
                      fontSize: 32,
                      fontWeight: FontWeight.w600,
                      color: RouteAtlasColors.ink,
                    ),
                  ),
                  const SizedBox(height: 10),
                  Text(
                    'Account type: ${widget.role}. This choice cannot be changed.',
                  ),
                  const SizedBox(height: 28),
                  _registrationField(
                    'Full name',
                    _name,
                    validator: (value) => (value?.trim().length ?? 0) < 2
                        ? 'Enter your full name.'
                        : null,
                    textCapitalization: TextCapitalization.words,
                  ),
                  const SizedBox(height: 18),
                  _registrationField(
                    'Email',
                    _email,
                    keyboardType: TextInputType.emailAddress,
                    validator: (value) => (value ?? '').contains('@')
                        ? null
                        : 'Enter a valid email address.',
                  ),
                  const SizedBox(height: 18),
                  _registrationField(
                    'Password',
                    _password,
                    obscureText: true,
                    validator: _passwordError,
                  ),
                  const SizedBox(height: 18),
                  _registrationField(
                    'Confirm password',
                    _confirmPassword,
                    obscureText: true,
                    validator: (value) => value == _password.text
                        ? null
                        : 'Passwords do not match.',
                  ),
                  const SizedBox(height: 10),
                  const Text(
                    'Use at least 8 characters with uppercase, lowercase, a number, and a symbol.',
                    style: TextStyle(
                      color: RouteAtlasColors.muted,
                      height: 1.4,
                    ),
                  ),
                  if (_errorMessage != null) ...[
                    const SizedBox(height: 14),
                    _MessageBanner(message: _errorMessage!, isError: true),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    onPressed: _submitting ? null : _register,
                    child: _submitting
                        ? const CircularProgressIndicator()
                        : const Text('Create account'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );

  String? _passwordError(String? value) {
    final password = value ?? '';
    if (password.length < 8 || password.length > 128) {
      return 'Use 8 to 128 characters.';
    }
    if (!RegExp(r'[a-z]').hasMatch(password) ||
        !RegExp(r'[A-Z]').hasMatch(password) ||
        !RegExp(r'\d').hasMatch(password) ||
        !RegExp(r'[^A-Za-z0-9]').hasMatch(password)) {
      return 'Add uppercase, lowercase, a number, and a symbol.';
    }
    return null;
  }

  Widget _registrationField(
    String label,
    TextEditingController controller, {
    String? Function(String?)? validator,
    TextInputType keyboardType = TextInputType.text,
    TextCapitalization textCapitalization = TextCapitalization.none,
    bool obscureText = false,
  }) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
      const SizedBox(height: 7),
      TextFormField(
        controller: controller,
        validator: validator,
        keyboardType: keyboardType,
        textCapitalization: textCapitalization,
        obscureText: obscureText,
        decoration: InputDecoration(hintText: label),
      ),
    ],
  );
}

class EmailOtpScreen extends ConsumerStatefulWidget {
  const EmailOtpScreen({required this.email, super.key});

  final String email;

  @override
  ConsumerState<EmailOtpScreen> createState() => _EmailOtpScreenState();
}

class _EmailOtpScreenState extends ConsumerState<EmailOtpScreen> {
  final _code = TextEditingController();
  bool _submitting = false;
  String? _errorMessage;

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  Future<void> _verify() async {
    if (!RegExp(r'^\d{6}$').hasMatch(_code.text)) {
      setState(() => _errorMessage = 'Enter the 6-digit code from your email.');
      return;
    }
    setState(() {
      _submitting = true;
      _errorMessage = null;
    });
    try {
      await ref
          .read(authRepositoryProvider)
          .verifyEmail(email: widget.email, otp: _code.text);
      if (mounted) context.go('/login?verified=1');
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _errorMessage = failure.message);
    } catch (_) {
      if (mounted) {
        setState(() => _errorMessage = 'Verification failed. Try again.');
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => _page(
    context,
    SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextButton.icon(
              onPressed: () => context.pop(),
              icon: const Icon(Icons.arrow_back),
              label: const Text('Back'),
              style: TextButton.styleFrom(alignment: Alignment.centerLeft),
            ),
            const SizedBox(height: 24),
            Text(
              'Verify your email',
              style: GoogleFonts.barlow(
                fontSize: 32,
                fontWeight: FontWeight.w600,
                color: RouteAtlasColors.ink,
              ),
            ),
            const SizedBox(height: 10),
            Text('Enter the 6-digit code sent to ${widget.email}.'),
            const SizedBox(height: 24),
            TextField(
              controller: _code,
              keyboardType: TextInputType.number,
              maxLength: 6,
              inputFormatters: [FilteringTextInputFormatter.digitsOnly],
              textAlign: TextAlign.center,
              decoration: const InputDecoration(hintText: '000000'),
            ),
            const SizedBox(height: 8),
            const Text(
              'Codes expire after 10 minutes. Code resend is unavailable until the API provides a resend endpoint.',
              style: TextStyle(color: RouteAtlasColors.muted, height: 1.4),
            ),
            if (_errorMessage != null) ...[
              const SizedBox(height: 14),
              _MessageBanner(message: _errorMessage!, isError: true),
            ],
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _submitting ? null : _verify,
              child: _submitting
                  ? const CircularProgressIndicator()
                  : const Text('Verify email'),
            ),
          ],
        ),
      ),
    ),
  );
}

class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  ConsumerState<ForgotPasswordScreen> createState() =>
      _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen> {
  final _email = TextEditingController();
  bool _submitting = false;
  String? _message;
  bool _failed = false;

  @override
  void dispose() {
    _email.dispose();
    super.dispose();
  }

  Future<void> _sendResetCode() async {
    if (!_email.text.contains('@')) {
      setState(() {
        _message = 'Enter a valid email address.';
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
          .read(authRepositoryProvider)
          .forgotPassword(email: _email.text.trim());
      if (mounted) {
        context.go(
          '/reset-password?email=${Uri.encodeQueryComponent(_email.text.trim())}',
        );
      }
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() {
          _message = failure.message;
          _failed = true;
        });
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => _page(
    context,
    SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextButton.icon(
              onPressed: () => context.pop(),
              icon: const Icon(Icons.arrow_back),
              label: const Text('Back to sign in'),
              style: TextButton.styleFrom(alignment: Alignment.centerLeft),
            ),
            const SizedBox(height: 24),
            Text(
              'Reset your password',
              style: GoogleFonts.barlow(
                fontSize: 32,
                fontWeight: FontWeight.w600,
                color: RouteAtlasColors.ink,
              ),
            ),
            const SizedBox(height: 10),
            const Text('Enter your email and we will send a reset code.'),
            const SizedBox(height: 24),
            TextField(
              controller: _email,
              keyboardType: TextInputType.emailAddress,
              autofillHints: const [AutofillHints.email],
              decoration: const InputDecoration(labelText: 'Email'),
            ),
            if (_message != null) ...[
              const SizedBox(height: 14),
              _MessageBanner(message: _message!, isError: _failed),
            ],
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _submitting ? null : _sendResetCode,
              child: _submitting
                  ? const CircularProgressIndicator()
                  : const Text('Send reset code'),
            ),
          ],
        ),
      ),
    ),
  );
}

class GoogleOAuthScreen extends ConsumerStatefulWidget {
  const GoogleOAuthScreen({required this.authorizationUrl, super.key});

  final String authorizationUrl;

  @override
  ConsumerState<GoogleOAuthScreen> createState() => _GoogleOAuthScreenState();
}

class _GoogleOAuthScreenState extends ConsumerState<GoogleOAuthScreen> {
  late final WebViewController _controller;
  String? _errorMessage;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setNavigationDelegate(
        NavigationDelegate(
          onPageStarted: (_) => setState(() => _loading = true),
          onPageFinished: (_) => setState(() => _loading = false),
          onNavigationRequest: _onNavigationRequest,
        ),
      )
      ..loadRequest(Uri.parse(widget.authorizationUrl));
  }

  NavigationDecision _onNavigationRequest(NavigationRequest request) {
    final uri = Uri.tryParse(request.url);
    if (uri != null && uri.path.endsWith('/auth/google/callback')) {
      final code = uri.queryParameters['code'];
      final state = uri.queryParameters['state'];
      if (code != null && state != null) unawaited(_finish(code, state));
      return NavigationDecision.prevent;
    }
    return NavigationDecision.navigate;
  }

  Future<void> _finish(String code, String state) async {
    try {
      final session = await ref
          .read(authRepositoryProvider)
          .completeGoogleAuthorization(code: code, state: state);
      if (mounted) context.pop(session);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _errorMessage = failure.message);
    } catch (_) {
      if (mounted) {
        setState(
          () => _errorMessage = 'Google sign-in could not be completed.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) => _page(
    context,
    SafeArea(
      child: Column(
        children: [
          Row(
            children: [
              IconButton(
                tooltip: 'Close Google sign-in',
                onPressed: () => context.pop(),
                icon: const Icon(Icons.close),
              ),
              const Expanded(child: Text('Sign in with Google')),
              if (_loading)
                const Padding(
                  padding: EdgeInsets.only(right: 16),
                  child: SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                ),
            ],
          ),
          if (_errorMessage != null)
            Padding(
              padding: const EdgeInsets.all(16),
              child: _MessageBanner(message: _errorMessage!, isError: true),
            ),
          Expanded(child: WebViewWidget(controller: _controller)),
        ],
      ),
    ),
  );
}

Widget _page(BuildContext context, Widget child) {
  if (Platform.isIOS) {
    return CupertinoPageScaffold(
      backgroundColor: RouteAtlasColors.canvas,
      child: child,
    );
  }
  return Scaffold(backgroundColor: RouteAtlasColors.canvas, body: child);
}

class _BrandWordmark extends StatelessWidget {
  const _BrandWordmark();

  @override
  Widget build(BuildContext context) => Row(
    children: [
      const CustomPaint(size: Size(44, 34), painter: _RouteMarkPainter()),
      const SizedBox(width: 10),
      Text(
        'PakiCarpool',
        style: GoogleFonts.barlow(
          color: RouteAtlasColors.ink,
          fontSize: 22,
          fontWeight: FontWeight.w600,
        ),
      ),
    ],
  );
}

class _RouteMarkPainter extends CustomPainter {
  const _RouteMarkPainter();

  @override
  void paint(Canvas canvas, Size size) {
    final routePaint = Paint()
      ..color = RouteAtlasColors.teal
      ..strokeWidth = 3
      ..strokeCap = StrokeCap.round
      ..style = PaintingStyle.stroke;
    final path = Path()
      ..moveTo(6, size.height - 7)
      ..lineTo(size.width / 2, 9)
      ..lineTo(size.width - 6, size.height - 7);
    canvas.drawPath(path, routePaint);
    canvas.drawCircle(
      Offset(6, size.height - 7),
      4.5,
      Paint()..color = RouteAtlasColors.saffron,
    );
    canvas.drawCircle(
      Offset(size.width / 2, 9),
      4,
      Paint()..color = RouteAtlasColors.teal,
    );
    canvas.drawCircle(
      Offset(size.width - 6, size.height - 7),
      4.5,
      Paint()..color = RouteAtlasColors.teal,
    );
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class _AlternativeDivider extends StatelessWidget {
  const _AlternativeDivider();

  @override
  Widget build(BuildContext context) => Row(
    children: [
      const Expanded(child: Divider(color: RouteAtlasColors.outline)),
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12),
        child: Text(
          'or',
          style: Theme.of(context).textTheme.bodySmall
              ?.copyWith(color: RouteAtlasColors.secondaryMuted),
        ),
      ),
      const Expanded(child: Divider(color: RouteAtlasColors.outline)),
    ],
  );
}

class _MessageBanner extends StatelessWidget {
  const _MessageBanner({required this.message, required this.isError});

  final String message;
  final bool isError;

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    child: Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: isError
            ? RouteAtlasColors.errorSurface
            : RouteAtlasColors.tealSurface,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(
        message,
        style: TextStyle(
          color: isError ? RouteAtlasColors.coral : RouteAtlasColors.ink,
          height: 1.35,
        ),
      ),
    ),
  );
}
