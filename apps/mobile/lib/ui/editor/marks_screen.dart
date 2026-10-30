import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../api/api_client.dart';
import '../../api/edit_api.dart';
import '../common.dart';
import 'page_stage.dart';

/// Tick / cross: tap a box on the form to place a mark; tap a mark to select, drag to move, resize or delete.
class MarksScreen extends StatefulWidget {
  const MarksScreen({super.key, required this.documentId, required this.pageId});

  final String documentId;
  final String pageId;

  @override
  State<MarksScreen> createState() => _MarksScreenState();
}

class _MarksScreenState extends State<MarksScreen> {
  static const _colors = ['#000000', '#1565C0', '#C62828'];
  PageImage? _page;
  List<PageMark> _marks = const [];
  String? _selectedId;
  PageBox? _dragBox;
  String _kind = 'Check';
  String _color = _colors.first;
  double _scale = 1.2;
  bool _busy = false;
  Object? _error;

  EditApi get _edits => context.read<EditApi>();

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        PageImage.load(context.read<DocumentsApi>(), widget.documentId, widget.pageId),
        _edits.marks(widget.documentId, widget.pageId),
      ]);
      if (!mounted) return;
      setState(() {
        _page = results[0] as PageImage;
        _marks = results[1] as List<PageMark>;
      });
    } catch (error) {
      if (mounted) setState(() => _error = error);
    }
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
      await _load();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  PageMark? get _selected => _marks.where((mark) => mark.id == _selectedId).firstOrNull;

  /// Same placement as the web (markBoxAt): square in pixels, centred on the tap.
  PageBox _boxAt(Offset point) {
    final size = _page!.size;
    final width = .025 * _scale;
    final height = width * size.width / size.height;
    return PageBox(point.dx - width / 2, point.dy - height / 2, width, height).clamp();
  }

  void _tap(Offset point) {
    final hit = _marks.reversed.where((mark) {
      final box = mark.box;
      return point.dx >= box.x - .01 && point.dx <= box.right + .01 && point.dy >= box.y - .01 && point.dy <= box.bottom + .01;
    }).firstOrNull;
    if (hit != null) {
      setState(() => _selectedId = hit.id);
      return;
    }
    if (_selectedId != null) {
      setState(() => _selectedId = null);
      return;
    }
    _run(() async {
      final mark = await _edits.addMark(widget.documentId, widget.pageId,
          kind: _kind, box: _boxAt(point), color: _color, strokeWidth: .08);
      if (mounted) setState(() => _marks = [..._marks, mark]);
    });
  }

  void _saveMove(PageMark mark) {
    final box = _dragBox;
    _dragBox = null;
    if (box == null) return;
    _run(() async {
      final updated = await _edits.updateMark(widget.documentId, widget.pageId, mark, box: box);
      if (mounted) setState(() => _marks = [for (final item in _marks) item.id == updated.id ? updated : item]);
    });
  }

  void _recolor(String color) {
    setState(() => _color = color);
    final mark = _selected;
    if (mark == null) return;
    _run(() async {
      final updated = await _edits.updateMark(widget.documentId, widget.pageId, mark, color: color);
      if (mounted) setState(() => _marks = [for (final item in _marks) item.id == updated.id ? updated : item]);
    });
  }

  void _delete(PageMark mark) => _run(() async {
        await _edits.deleteMark(widget.documentId, widget.pageId, mark);
        if (mounted) {
          setState(() {
            _marks = _marks.where((item) => item.id != mark.id).toList();
            _selectedId = null;
          });
        }
      });

  @override
  Widget build(BuildContext context) {
    final page = _page;
    final l = context.l;
    final selected = _selected;
    return Scaffold(
      appBar: AppBar(title: Text(l.toolMarks), actions: [
        if (selected != null)
          IconButton(tooltip: l.delete, onPressed: _busy ? null : () => _delete(selected), icon: const Icon(Icons.delete_outline)),
      ]),
      body: _error != null
          ? Center(child: Text(friendlyError(context, _error!)))
          : page == null
              ? const Center(child: CircularProgressIndicator())
              : Column(children: [
                  if (_busy) const LinearProgressIndicator(),
                  Padding(padding: const EdgeInsets.all(8), child: Text(l.marksHint, textAlign: TextAlign.center)),
                  Expanded(
                    child: PageStage(
                      image: MemoryImage(page.bytes),
                      imageSize: page.size,
                      onTapPage: _busy ? null : _tap,
                      builder: (context, rect) => [
                        for (final mark in _marks)
                          if (mark.id == _selectedId)
                            DraggableBox(
                              pageRect: rect,
                              box: _dragBox ?? mark.box,
                              keepAspect: 1,
                              onChanged: (box) => setState(() => _dragBox = box),
                              onChangeEnd: () => _saveMove(mark),
                              child: CustomPaint(painter: MarkPainter(mark.kind, mark.color, mark.strokeWidth)),
                            )
                          else
                            Positioned.fromRect(
                              rect: PageStage.toRect(rect, mark.box),
                              child: IgnorePointer(
                                  child: CustomPaint(painter: MarkPainter(mark.kind, mark.color, mark.strokeWidth))),
                            ),
                      ],
                    ),
                  ),
                ]),
      bottomNavigationBar: page == null
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(8),
                child: Column(mainAxisSize: MainAxisSize.min, children: [
                  Row(children: [
                    SegmentedButton<String>(
                      segments: [
                        ButtonSegment(value: 'Check', icon: const Icon(Icons.check), label: Text(l.tick)),
                        ButtonSegment(value: 'Cross', icon: const Icon(Icons.close), label: Text(l.cross)),
                      ],
                      selected: {_kind},
                      onSelectionChanged: (value) => setState(() => _kind = value.first),
                    ),
                    const Spacer(),
                    for (final color in _colors)
                      GestureDetector(
                        onTap: () => _recolor(color),
                        child: Container(
                          margin: const EdgeInsets.all(4),
                          width: 32,
                          height: 32,
                          decoration: BoxDecoration(
                            color: parseHex(color),
                            shape: BoxShape.circle,
                            border: Border.all(width: _color == color ? 3 : 1,
                                color: _color == color ? Theme.of(context).colorScheme.primary : Colors.grey),
                          ),
                        ),
                      ),
                  ]),
                  Row(children: [
                    Text(l.size),
                    Expanded(child: Slider(value: _scale, min: .5, max: 3, onChanged: (value) => setState(() => _scale = value))),
                  ]),
                ]),
              ),
            ),
    );
  }
}

Color parseHex(String hex) => Color(int.parse('FF${hex.substring(1, 7)}', radix: 16));

/// Draws a tick or cross like the server's PDF renderer: 100×100 path, pen = min(w, h) × strokeWidth.
class MarkPainter extends CustomPainter {
  MarkPainter(this.kind, this.color, this.strokeWidth);

  final String kind;
  final String color;
  final double strokeWidth;

  @override
  void paint(Canvas canvas, Size size) {
    final pen = Paint()
      ..color = parseHex(color)
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round
      ..strokeWidth = size.shortestSide * strokeWidth;
    Offset p(double x, double y) => Offset(x / 100 * size.width, y / 100 * size.height);
    final path = Path();
    if (kind == 'Check') {
      path
        ..moveTo(p(18, 52).dx, p(18, 52).dy)
        ..lineTo(p(42, 76).dx, p(42, 76).dy)
        ..lineTo(p(82, 24).dx, p(82, 24).dy);
    } else {
      path
        ..moveTo(p(24, 24).dx, p(24, 24).dy)
        ..lineTo(p(76, 76).dx, p(76, 76).dy)
        ..moveTo(p(76, 24).dx, p(76, 24).dy)
        ..lineTo(p(24, 76).dx, p(24, 76).dy);
    }
    canvas.drawPath(path, pen);
  }

  @override
  bool shouldRepaint(MarkPainter old) => old.kind != kind || old.color != color || old.strokeWidth != strokeWidth;
}
