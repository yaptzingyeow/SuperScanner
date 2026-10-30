import 'dart:async';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import 'common.dart';
import 'editor/eraser_screen.dart';
import 'editor/marks_screen.dart';
import 'editor/signature_screen.dart';
import 'editor/text_screen.dart';

/// One page: zoomable preview, the scan-look strip, rotate, auto-detect and corner adjustment.
class PageScreen extends StatefulWidget {
  const PageScreen({super.key, required this.documentId, required this.pageId});

  final String documentId;
  final String pageId;

  @override
  State<PageScreen> createState() => _PageScreenState();
}

class _PageScreenState extends State<PageScreen> {
  CropState? _crop;
  DocumentPage? _page;
  Object? _error;
  bool _working = false;
  Timer? _poll;

  DocumentsApi get _api => context.read<DocumentsApi>();

  static const _fullPage = [CropPoint(0, 0), CropPoint(1, 0), CropPoint(1, 1), CropPoint(0, 1)];

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        _api.getCrop(widget.documentId, widget.pageId),
        _api.getDocument(widget.documentId),
      ]);
      if (!mounted) return;
      final document = results[1] as DocumentDetail;
      final page = document.pages.where((item) => item.id == widget.pageId).firstOrNull;
      setState(() {
        _crop = results[0] as CropState;
        _page = page;
        _error = null;
      });
      _poll?.cancel();
      if (page != null && !page.isSettled && !page.isFailed) _poll = Timer(const Duration(seconds: 1), _load);
    } catch (error) {
      if (mounted) setState(() => _error = error);
    }
  }

  Future<void> _apply({String? filter, int? rotation, List<CropPoint>? points}) async {
    final crop = _crop;
    if (crop == null || _working) return;
    setState(() => _working = true);
    try {
      await _api.applyCrop(widget.documentId, widget.pageId,
          revision: crop.revision,
          points: points ?? crop.points ?? _fullPage,
          filter: filter ?? crop.filter,
          rotation: rotation ?? crop.rotation);
      await _load();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _detect() async {
    final crop = _crop;
    if (crop == null || _working) return;
    setState(() => _working = true);
    try {
      await _api.detectCrop(widget.documentId, widget.pageId, crop.revision);
      await _load();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  Future<void> _adjustCorners() async {
    final crop = _crop;
    if (crop == null) return;
    final points = await Navigator.of(context).push<List<CropPoint>>(MaterialPageRoute(
      fullscreenDialog: true,
      builder: (_) => CornerEditor(documentId: widget.documentId, pageId: widget.pageId,
          initial: crop.points ?? _fullPage, api: _api),
    ));
    if (points != null) await _apply(points: points);
  }

  @override
  Widget build(BuildContext context) {
    final page = _page;
    final crop = _crop;
    final settled = page?.isSettled ?? false;
    return Scaffold(
      appBar: AppBar(title: Text(page == null ? context.l.page : context.l.pageNumber(page.position + 1))),
      body: _error != null
          ? Center(child: Text(friendlyError(context, _error!)))
          : page == null || crop == null
              ? const Center(child: CircularProgressIndicator())
              : Column(children: [
                  if (_working || !settled) const LinearProgressIndicator(),
                  Expanded(
                    child: InteractiveViewer(
                      maxScale: 6,
                      child: Center(
                        child: ApiImage(documentId: widget.documentId, pageId: widget.pageId, asset: 'preview',
                            revision: page.previewRevision),
                      ),
                    ),
                  ),
                  SizedBox(
                    height: 56,
                    child: ListView(
                      scrollDirection: Axis.horizontal,
                      padding: const EdgeInsets.symmetric(horizontal: 8),
                      children: [
                        for (final look in scanLooks)
                          Padding(
                            padding: const EdgeInsets.all(4),
                            child: ChoiceChip(
                              label: Text(lookLabel(context, look)),
                              selected: crop.filter == look,
                              onSelected: _working || !settled ? null : (_) => _apply(filter: look),
                            ),
                          ),
                      ],
                    ),
                  ),
                ]),
      bottomNavigationBar: crop == null
          ? null
          : SafeArea(
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                const Divider(height: 1),
                Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: [
                  _ToolButton(icon: Icons.cleaning_services_outlined, label: context.l.toolEraser,
                      onPressed: _working || !settled ? null : () => _openTool(EraserScreen.new)),
                  _ToolButton(icon: Icons.text_fields, label: context.l.toolText,
                      onPressed: _working || !settled ? null : () => _openTool(TextScreen.new)),
                  _ToolButton(icon: Icons.draw_outlined, label: context.l.toolSignShort,
                      onPressed: _working || !settled ? null : () => _openTool(SignatureScreen.new)),
                  _ToolButton(icon: Icons.check_box_outlined, label: context.l.toolMarksShort,
                      onPressed: _working || !settled ? null : () => _openTool(MarksScreen.new)),
                ]),
                const Divider(height: 1),
                Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: [
                IconButton(
                  tooltip: context.l.rotateLeft,
                  onPressed: _working || !settled ? null : () => _apply(rotation: (crop.rotation + 270) % 360),
                  icon: const Icon(Icons.rotate_left),
                ),
                IconButton(
                  tooltip: context.l.rotateRight,
                  onPressed: _working || !settled ? null : () => _apply(rotation: (crop.rotation + 90) % 360),
                  icon: const Icon(Icons.rotate_right),
                ),
                TextButton.icon(
                  onPressed: _working || !settled ? null : _adjustCorners,
                  icon: const Icon(Icons.crop_free),
                  label: Text(context.l.corners),
                ),
                TextButton.icon(
                  onPressed: _working || !settled ? null : _detect,
                  icon: const Icon(Icons.auto_fix_high),
                  label: Text(context.l.autoDetect),
                ),
                ]),
              ]),
            ),
    );
  }

  /// Opens an edit tool full screen; reloads the page afterwards (edits change the page image).
  Future<void> _openTool(Widget Function({Key? key, required String documentId, required String pageId}) tool) async {
    await Navigator.of(context).push(MaterialPageRoute<Object?>(
        builder: (_) => tool(documentId: widget.documentId, pageId: widget.pageId)));
    if (mounted) await _load();
  }
}

class _ToolButton extends StatelessWidget {
  const _ToolButton({required this.icon, required this.label, required this.onPressed});

  final IconData icon;
  final String label;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) => TextButton(
        onPressed: onPressed,
        child: Column(mainAxisSize: MainAxisSize.min, children: [Icon(icon), const SizedBox(height: 2), Text(label)]),
      );
}

/// Full-screen corner adjustment on the uncropped photo, with large handles and a magnifier while dragging.
class CornerEditor extends StatefulWidget {
  const CornerEditor({super.key, required this.documentId, required this.pageId, required this.initial,
      required this.api});

  final String documentId;
  final String pageId;
  final List<CropPoint> initial;
  final DocumentsApi api;

  @override
  State<CornerEditor> createState() => _CornerEditorState();
}

class _CornerEditorState extends State<CornerEditor> {
  late final List<Offset> _points = [for (final point in widget.initial) Offset(point.x, point.y)];
  Uint8List? _bytes;
  Size? _imageSize;
  int? _dragging;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _loadImage();
  }

  Future<void> _loadImage() async {
    try {
      final bytes = await widget.api.pageImage(widget.documentId, widget.pageId, 'crop-source');
      final image = await decodeImageFromList(bytes);
      if (!mounted) return;
      setState(() {
        _bytes = bytes;
        _imageSize = Size(image.width.toDouble(), image.height.toDouble());
      });
    } catch (error) {
      if (mounted) setState(() => _error = error);
    }
  }

  /// Where the image sits inside [box] with BoxFit.contain.
  Rect _imageRect(Size box) {
    final image = _imageSize!;
    final scale = (box.width / image.width).clamp(0.0, box.height / image.height);
    final size = Size(image.width * scale, image.height * scale);
    return Offset((box.width - size.width) / 2, (box.height - size.height) / 2) & size;
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        backgroundColor: Colors.black,
        appBar: AppBar(
          title: Text(context.l.adjustCorners),
          actions: [
            TextButton(
              onPressed: _bytes == null
                  ? null
                  : () => Navigator.pop(context, [for (final point in _points) CropPoint(point.dx, point.dy)]),
              child: Text(context.l.apply),
            ),
          ],
        ),
        body: _error != null
            ? Center(child: Text(friendlyError(context, _error!), style: const TextStyle(color: Colors.white)))
            : _bytes == null
                ? const Center(child: CircularProgressIndicator())
                : Padding(
                    padding: const EdgeInsets.all(24),
                    child: LayoutBuilder(builder: (context, constraints) {
                      final rect = _imageRect(constraints.biggest);
                      Offset toScreen(Offset point) =>
                          rect.topLeft + Offset(point.dx * rect.width, point.dy * rect.height);
                      return GestureDetector(
                        onPanStart: (details) {
                          var nearest = 0;
                          for (var index = 1; index < 4; index++) {
                            if ((toScreen(_points[index]) - details.localPosition).distance <
                                (toScreen(_points[nearest]) - details.localPosition).distance) {
                              nearest = index;
                            }
                          }
                          if ((toScreen(_points[nearest]) - details.localPosition).distance < 64) {
                            setState(() => _dragging = nearest);
                          }
                        },
                        onPanUpdate: (details) {
                          final index = _dragging;
                          if (index == null) return;
                          final local = details.localPosition - rect.topLeft;
                          setState(() => _points[index] = Offset(
                              (local.dx / rect.width).clamp(0.0, 1.0), (local.dy / rect.height).clamp(0.0, 1.0)));
                        },
                        onPanEnd: (_) => setState(() => _dragging = null),
                        child: Stack(children: [
                          Positioned.fromRect(rect: rect, child: Image.memory(_bytes!, fit: BoxFit.fill)),
                          Positioned.fill(
                            child: CustomPaint(painter: _CornerPainter([for (final p in _points) toScreen(p)], _dragging)),
                          ),
                          if (_dragging != null)
                            Positioned(
                              left: 8,
                              top: 8,
                              child: RawMagnifier(
                                size: const Size(120, 120),
                                magnificationScale: 2.5,
                                focalPointOffset: toScreen(_points[_dragging!]) - const Offset(68, 68),
                                decoration: const MagnifierDecoration(
                                    shape: CircleBorder(side: BorderSide(color: Colors.white, width: 2))),
                              ),
                            ),
                        ]),
                      );
                    }),
                  ),
      );
}

class _CornerPainter extends CustomPainter {
  _CornerPainter(this.points, this.active);

  final List<Offset> points;
  final int? active;

  @override
  void paint(Canvas canvas, Size size) {
    final outline = Paint()
      ..color = const Color(0xFF34C38F)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 3;
    canvas.drawPath(ui.Path()..addPolygon(points, true), outline);
    for (var index = 0; index < points.length; index++) {
      canvas.drawCircle(points[index], index == active ? 18 : 14, Paint()..color = const Color(0xCCFFFFFF));
      canvas.drawCircle(points[index], index == active ? 18 : 14, outline);
    }
  }

  @override
  bool shouldRepaint(_CornerPainter old) => old.points != points || old.active != active;
}
