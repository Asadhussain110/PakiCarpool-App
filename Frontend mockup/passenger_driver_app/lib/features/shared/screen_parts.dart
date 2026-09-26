import 'package:flutter/material.dart';

import '../../core/theme/route_atlas_theme.dart';

class LoadingPane extends StatelessWidget {
  const LoadingPane({this.label = 'Loading…', super.key});

  final String label;

  @override
  Widget build(BuildContext context) => Center(
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        const CircularProgressIndicator(),
        const SizedBox(height: 16),
        Text(label),
      ],
    ),
  );
}

class ErrorPane extends StatelessWidget {
  const ErrorPane({required this.message, this.onRetry, super.key});

  final String message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.cloud_off_outlined, size: 38),
          const SizedBox(height: 12),
          Text(message, textAlign: TextAlign.center),
          if (onRetry != null) ...[
            const SizedBox(height: 12),
            OutlinedButton(onPressed: onRetry, child: const Text('Try again')),
          ],
        ],
      ),
    ),
  );
}

class NoticeStrip extends StatelessWidget {
  const NoticeStrip({required this.message, this.isError = false, super.key});

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

class StateLabel extends StatelessWidget {
  const StateLabel(this.label, {this.color = RouteAtlasColors.teal, super.key});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(
        width: 8,
        height: 8,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      ),
      const SizedBox(width: 7),
      Text(
        label,
        style: TextStyle(color: color, fontWeight: FontWeight.w600),
      ),
    ],
  );
}

Future<String?> askForText(
  BuildContext context, {
  required String title,
  required String label,
  int maxLength = 500,
  int minLength = 1,
}) async {
  final controller = TextEditingController();
  final result = await showModalBottomSheet<String>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (context) => Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        8,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: _TextPrompt(
        title: title,
        label: label,
        controller: controller,
        maxLength: maxLength,
        minLength: minLength,
      ),
    ),
  );
  controller.dispose();
  return result;
}

class _TextPrompt extends StatefulWidget {
  const _TextPrompt({
    required this.title,
    required this.label,
    required this.controller,
    required this.maxLength,
    required this.minLength,
  });

  final String title;
  final String label;
  final TextEditingController controller;
  final int maxLength;
  final int minLength;

  @override
  State<_TextPrompt> createState() => _TextPromptState();
}

class _TextPromptState extends State<_TextPrompt> {
  String? _error;

  @override
  Widget build(BuildContext context) => SafeArea(
    child: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(widget.title, style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 16),
        TextField(
          controller: widget.controller,
          maxLength: widget.maxLength,
          minLines: 3,
          maxLines: 5,
          decoration: InputDecoration(
            labelText: widget.label,
            errorText: _error,
          ),
        ),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: () {
            final text = widget.controller.text.trim();
            if (text.length < widget.minLength) {
              setState(
                () => _error = 'Enter at least ${widget.minLength} characters.',
              );
              return;
            }
            Navigator.pop(context, text);
          },
          child: const Text('Continue'),
        ),
      ],
    ),
  );
}

int asInt(Object? value) => value is int ? value : int.tryParse('$value') ?? 0;

double asDouble(Object? value) =>
    value is num ? value.toDouble() : double.tryParse('$value') ?? 0;

Map<String, dynamic> asMap(Object? value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) return value.map((key, item) => MapEntry('$key', item));
  return const {};
}
