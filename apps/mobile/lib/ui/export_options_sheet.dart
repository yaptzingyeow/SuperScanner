import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/models.dart';
import '../l10n/app_localizations.dart';
import '../services/export_service.dart';
import 'common.dart';
import 'editor/marks_screen.dart' show parseHex;

/// Asks how to export before Share, Print or Save to phone. Returns null when cancelled; otherwise the
/// watermark to stamp on every page (null for none). The last choice is remembered for next time.
Future<({WatermarkSettings? watermark})?> showExportOptions(BuildContext context,
    {required String action, required Future<Uint8List> Function() firstPage}) async {
  final exports = context.read<ExportService>();
  final last = await exports.lastWatermark();
  if (!context.mounted) return null;
  final result = await showModalBottomSheet<({WatermarkSettings settings, bool enabled})>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    builder: (_) => _ExportOptionsSheet(action: action, firstPage: firstPage, initial: last.settings, enabled: last.enabled),
  );
  if (result == null) return null;
  await exports.rememberWatermark(result.settings, enabled: result.enabled);
  return (watermark: result.enabled ? result.settings : null);
}

class _ExportOptionsSheet extends StatefulWidget {
  const _ExportOptionsSheet({required this.action, required this.firstPage, required this.initial, required this.enabled});

  final String action;
  final Future<Uint8List> Function() firstPage;
  final WatermarkSettings initial;
  final bool enabled;

  @override
  State<_ExportOptionsSheet> createState() => _ExportOptionsSheetState();
}

class _ExportOptionsSheetState extends State<_ExportOptionsSheet> {
  static const _angles = [0.0, 35.0, 45.0, -45.0, 90.0];

  late WatermarkSettings _s = widget.initial;
  late bool _enabled = widget.enabled;
  late final _text = TextEditingController(text: widget.initial.text);
  late final Future<Uint8List> _page = widget.firstPage();
  bool _more = false;

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  void _set(WatermarkSettings next) => setState(() => _s = next);

  void _setText(String text) {
    _text.value = TextEditingValue(text: text, selection: TextSelection.collapsed(offset: text.length));
    _set(_s.copyWith(text: text));
  }

  List<String> _presets(AppLocalizations l) => [
        l.wmPresetGovernment, l.wmPresetIc, l.wmPresetConfidential, l.wmPresetCopy, l.wmPresetDraft, l.wmPresetSample,
      ];

  @override
  Widget build(BuildContext context) {
    final l = context.l;
    final canExport = !_enabled || _s.isValid;
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, mainAxisSize: MainAxisSize.min, children: [
          Text(l.exportOptions, style: Theme.of(context).textTheme.titleLarge),
          SwitchListTile(
            contentPadding: EdgeInsets.zero,
            title: Text(l.addWatermark),
            subtitle: Text(l.watermarkSubtitle),
            value: _enabled,
            onChanged: (value) => setState(() => _enabled = value),
          ),
          if (_enabled) ...[
            _Preview(page: _page, settings: _s),
            Padding(
              padding: const EdgeInsets.only(top: 4, bottom: 8),
              child: Text(l.wmPreviewNote, style: Theme.of(context).textTheme.bodySmall, textAlign: TextAlign.center),
            ),
            TextField(
              controller: _text,
              maxLength: WatermarkSettings.maxTextLength,
              decoration: InputDecoration(labelText: l.wmText, border: const OutlineInputBorder()),
              onChanged: (value) => _set(_s.copyWith(text: value)),
            ),
            Text(l.wmQuickText, style: Theme.of(context).textTheme.labelLarge),
            Wrap(spacing: 6, children: [
              for (final preset in _presets(l))
                ChoiceChip(label: Text(preset), selected: _s.text == preset, onSelected: (_) => _setText(preset)),
            ]),
            const SizedBox(height: 12),
            SegmentedButton<String>(
              segments: [
                ButtonSegment(value: 'Single', label: Text(l.wmOnce)),
                ButtonSegment(value: 'Tiled', label: Text(l.wmRepeated)),
              ],
              selected: {_s.layout},
              onSelectionChanged: (value) => _set(_s.copyWith(layout: value.first)),
            ),
            const SizedBox(height: 12),
            Row(children: [
              Text(l.wmColour),
              const SizedBox(width: 8),
              for (final color in WatermarkSettings.colors)
                GestureDetector(
                  onTap: () => _set(_s.copyWith(color: color)),
                  child: Container(
                    margin: const EdgeInsets.all(3),
                    width: 28,
                    height: 28,
                    decoration: BoxDecoration(
                      color: parseHex(color),
                      shape: BoxShape.circle,
                      border: Border.all(width: _s.color.toUpperCase() == color ? 3 : 1, color: Colors.grey),
                    ),
                  ),
                ),
            ]),
            _SliderRow(label: l.wmTransparency, value: _s.opacity, min: .05, max: 1, divisions: 95,
                display: '${(_s.opacity * 100).round()}%', onChanged: (v) => _set(_s.copyWith(opacity: v))),
            _SliderRow(label: l.wmSize, value: _s.sizePercent, min: 2, max: 25, divisions: 46,
                display: _s.sizePercent.toStringAsFixed(1), onChanged: (v) => _set(_s.copyWith(sizePercent: v))),
            _SliderRow(label: l.wmAngle, value: _s.angleDegrees, min: -90, max: 90, divisions: 180,
                display: '${_s.angleDegrees.round()}°', onChanged: (v) => _set(_s.copyWith(angleDegrees: v.roundToDouble()))),
            Wrap(spacing: 6, children: [
              for (final angle in _angles)
                ChoiceChip(
                  label: Text('${angle.round()}°'),
                  selected: _s.angleDegrees == angle,
                  onSelected: (_) => _set(_s.copyWith(angleDegrees: angle)),
                ),
            ]),
            TextButton.icon(
              onPressed: () => setState(() => _more = !_more),
              icon: Icon(_more ? Icons.expand_less : Icons.expand_more),
              label: Text(l.moreOptions),
            ),
            if (_more) ...[
              DropdownButtonFormField<String>(
                initialValue: _s.fontId,
                isExpanded: true,
                decoration: InputDecoration(labelText: l.wmFont, border: const OutlineInputBorder()),
                items: [
                  for (final font in WatermarkSettings.fonts) DropdownMenuItem(value: font.id, child: Text(font.label)),
                ],
                onChanged: (value) => value == null ? null : _set(_s.copyWith(fontId: value)),
              ),
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(l.wmBold),
                value: _s.bold && _s.fontHasBold,
                onChanged: _s.fontHasBold ? (value) => _set(_s.copyWith(bold: value)) : null,
              ),
              if (_s.isTiled)
                _SliderRow(label: l.wmSpacing, value: _s.spacing, min: .5, max: 4, divisions: 35,
                    display: _s.spacing.toStringAsFixed(1), onChanged: (v) => _set(_s.copyWith(spacing: v)))
              else ...[
                Text(l.wmPosition, style: Theme.of(context).textTheme.labelLarge),
                const SizedBox(height: 4),
                SegmentedButton<String>(
                  segments: [
                    ButtonSegment(value: 'Top', label: Text(l.wmTop)),
                    ButtonSegment(value: 'Center', label: Text(l.wmCenter)),
                    ButtonSegment(value: 'Bottom', label: Text(l.wmBottom)),
                  ],
                  selected: {_s.position},
                  onSelectionChanged: (value) => _set(_s.copyWith(position: value.first)),
                ),
              ],
              TextButton(
                onPressed: () {
                  _set(const WatermarkSettings());
                  _text.text = _s.text;
                },
                child: Text(l.wmReset),
              ),
            ],
          ],
          const SizedBox(height: 12),
          Row(children: [
            Expanded(child: OutlinedButton(onPressed: () => Navigator.pop(context), child: Text(l.cancel))),
            const SizedBox(width: 8),
            Expanded(
              child: FilledButton(
                onPressed: canExport ? () => Navigator.pop(context, (settings: _s, enabled: _enabled)) : null,
                child: Text(widget.action),
              ),
            ),
          ]),
        ]),
      ),
    );
  }
}

class _SliderRow extends StatelessWidget {
  const _SliderRow({required this.label, required this.value, required this.min, required this.max,
      required this.divisions, required this.display, required this.onChanged});

  final String label;
  final double value;
  final double min;
  final double max;
  final int divisions;
  final String display;
  final ValueChanged<double> onChanged;

  @override
  Widget build(BuildContext context) => Row(children: [
        SizedBox(width: 96, child: Text(label)),
        Expanded(
          child: Slider(value: value.clamp(min, max), min: min, max: max, divisions: divisions, onChanged: onChanged),
        ),
        SizedBox(width: 44, child: Text(display, textAlign: TextAlign.end)),
      ]);
}

/// Page 1 with the watermark drawn over it, laid out like the export worker (PdfUserWatermark).
class _Preview extends StatelessWidget {
  const _Preview({required this.page, required this.settings});

  final Future<Uint8List> page;
  final WatermarkSettings settings;

  @override
  Widget build(BuildContext context) => SizedBox(
        height: 260,
        child: FutureBuilder<Uint8List>(
          future: page,
          builder: (context, snapshot) {
            final bytes = snapshot.data;
            if (bytes == null) {
              return Center(child: snapshot.hasError ? const Icon(Icons.image_not_supported_outlined) : const CircularProgressIndicator());
            }
            return Center(
              child: ClipRect(
                child: Stack(fit: StackFit.passthrough, children: [
                  Image.memory(bytes, fit: BoxFit.contain, gaplessPlayback: true,
                      // A blank A4 page still shows how the watermark sits.
                      errorBuilder: (_, _, _) => const SizedBox(width: 184, height: 260, child: ColoredBox(color: Colors.white))),
                  Positioned.fill(child: CustomPaint(painter: WatermarkPainter(settings))),
                ]),
              ),
            );
          },
        ),
      );
}

/// Draws [settings] over a page of the painted size, using the server's geometry: text height is a
/// share of the shorter side; Single sits at 15% / 50% / 85% height; Tiled is a rotated brick pattern.
class WatermarkPainter extends CustomPainter {
  WatermarkPainter(this.settings);

  final WatermarkSettings settings;

  /// Copies drawn at most; the worker caps its tiles the same way.
  static const maxTiles = 400;

  @override
  void paint(Canvas canvas, Size size) {
    final text = settings.text.trim();
    if (text.isEmpty || size.isEmpty) return;
    final textHeight = math.min(size.width, size.height) * settings.sizePercent / 100;
    final painter = TextPainter(
      text: TextSpan(
        text: text,
        style: TextStyle(
          fontSize: textHeight,
          height: 1.35,
          fontWeight: settings.bold && settings.fontHasBold ? FontWeight.bold : FontWeight.normal,
          color: parseHex(settings.color).withValues(alpha: settings.opacity),
        ),
      ),
      textDirection: TextDirection.ltr,
      maxLines: 1,
    )..layout();
    final width = painter.width;
    final height = painter.height;
    final angle = -settings.angleDegrees * math.pi / 180;

    canvas.save();
    canvas.clipRect(Offset.zero & size);
    if (!settings.isTiled) {
      final centerY = switch (settings.position) {
        'Top' => size.height * .15,
        'Bottom' => size.height * .85,
        _ => size.height / 2,
      };
      canvas
        ..translate(size.width / 2, centerY)
        ..rotate(angle);
      painter.paint(canvas, Offset(-width / 2, -height / 2));
      canvas.restore();
      return;
    }

    final gap = textHeight * settings.spacing;
    var stepX = width + gap * 1.5;
    var stepY = height + gap;
    final radius = math.sqrt(size.width * size.width + size.height * size.height) / 2 + math.max(width, height);
    int columns() => (2 * radius / stepX).ceil() + 1;
    int rows() => (2 * radius / stepY).ceil() + 1;
    while (columns() * rows() > maxTiles) {
      stepX *= 1.15;
      stepY *= 1.15;
    }
    canvas
      ..translate(size.width / 2, size.height / 2)
      ..rotate(angle);
    for (var row = 0; row < rows(); row++) {
      final y = -radius + row * stepY;
      final offset = row.isEven ? 0 : stepX / 2;
      for (var column = 0; column < columns(); column++) {
        final x = -radius - stepX / 2 + offset + column * stepX;
        painter.paint(canvas, Offset(x - width / 2, y - height / 2));
      }
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(WatermarkPainter oldDelegate) => oldDelegate.settings != settings;
}
