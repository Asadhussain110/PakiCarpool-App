import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/route_atlas_theme.dart';

class HCaptchaChallenge extends StatefulWidget {
  const HCaptchaChallenge({
    required this.siteKey,
    required this.onToken,
    super.key,
  });

  final String siteKey;
  final ValueChanged<String> onToken;

  @override
  State<HCaptchaChallenge> createState() => _HCaptchaChallengeState();
}

class _HCaptchaChallengeState extends State<HCaptchaChallenge> {
  late final WebViewController _controller;

  String get apiOrigin => Uri.parse(apiBaseUrl).origin;

  bool get _supportsWebView =>
      defaultTargetPlatform == TargetPlatform.android ||
      defaultTargetPlatform == TargetPlatform.iOS;

  @override
  void initState() {
    super.initState();
    if (widget.siteKey.isEmpty || !_supportsWebView) return;
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..addJavaScriptChannel(
        'CaptchaChannel',
        onMessageReceived: (message) {
          if (message.message.isNotEmpty && message.message != 'ERROR') {
            widget.onToken(message.message);
          }
        },
      )
      ..loadHtmlString(_challengeHtml(widget.siteKey), baseUrl: apiOrigin);
  }

  @override
  Widget build(BuildContext context) {
    if (widget.siteKey.isEmpty) {
      return const _ChallengeMessage(
        message: 'Security verification is not configured for this build.',
      );
    }
    if (!_supportsWebView) {
      return const _ChallengeMessage(
        message: 'Security verification is available on the mobile app.',
      );
    }
    return ClipRRect(
      borderRadius: BorderRadius.circular(8),
      child: SizedBox(
        height: 112,
        child: WebViewWidget(controller: _controller),
      ),
    );
  }

  String _challengeHtml(String siteKey) =>
      '''
<!doctype html>
<html><head>
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1">
<script src="https://js.hcaptcha.com/1/api.js?onload=onloadCallback&render=explicit" async defer></script>
<script>
function onloadCallback() {
  hcaptcha.render('captcha', {
    sitekey: '$siteKey',
    callback: function(token) { CaptchaChannel.postMessage(token); },
    'error-callback': function() { CaptchaChannel.postMessage('ERROR'); }
  });
}
</script>
<style>body{margin:0;padding:8px;background:#F5F8F6;font-family:sans-serif}</style>
</head><body><div id="captcha"></div></body></html>
''';
}

class _ChallengeMessage extends StatelessWidget {
  const _ChallengeMessage({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: RouteAtlasColors.errorSurface,
      borderRadius: BorderRadius.circular(8),
    ),
    child: Text(message, style: const TextStyle(color: RouteAtlasColors.ink)),
  );
}
