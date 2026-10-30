import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../api/api_client.dart';
import '../../api/edit_api.dart';
import '../common.dart';
import 'page_stage.dart';

/// Signatures: draw with a finger or use a photo (the server removes the paper), then place and resize.
/// A visual signature only; it is drawn on the page when the PDF is exported.
class SignatureScreen extends StatefulWidget {
  const SignatureScreen({super.key, required this.documentId, required this.pageId});

  final String documentId;
  final String pageId;

  @override
  State<SignatureScreen> createState() => _SignatureScreenState();
}

class _SignatureScreenState extends State<SignatureScreen> {
  PageImage? _page;
  List<PageSignature> _signatures = const [];
  final _images = <String, Uint8List>{};
  String? _selectedId;
  PageBox? _dragBox;
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
        _edits.signatures(widget.documentId, widget.pageId),
      ]);
      final signatures = results[1] as List<PageSignature>;
      for (final signature in signatures) {
        _images[signature.id] ??= await _edits.signatureImage(widget.documentId, widget.pageId, signature.id);
      }
      if (!mounted) return;
      setState(() {
        _page = results[0] as PageImage;
        _signatures = signatures;
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
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _add(Future<Uint8List?> Function() source) async {
    final png = await source();
    if (png == null || !mounted) return;
    await _run(() async {
      final image = await PageImage.decodeImage(png);
      final aspect = image.width / image.height;
      final page = _page!.size;
      const width = .3;
      final height = (width * page.width / aspect) / page.height;
      final signature = await _edits.addSignature(widget.documentId, widget.pageId, png,
          PageBox((1 - width) / 2, .6, width, height).clamp());
      _images[signature.id] = png;
      if (mounted) {
        setState(() {
          _signatures = [..._signatures, signature];
          _selectedId = signature.id;
        });
      }
    });
  }

  Future<Uint8List?> _draw() => Navigator.of(context).push<Uint8List>(
      MaterialPageRoute(fullscreenDialog: true, builder: (_) => const SignaturePad()));

  Future<Uint8List?> _photo() async {
    final picked = await FilePicker.pickFiles(type: FileType.custom, allowedExtensions: const ['jpg', 'jpeg', 'png']);
    if (picked.isEmpty) return null;
    final file = picked.first;
    return _edits.prepareSignature(await file.readAsBytes(), file.name);
  }

  void _saveMove(PageSignature signature) {
    final box = _dragBox;
    _dragBox = null;
    if (box == null) return;
    _run(() async {
      final updated = await _edits.moveSignature(widget.documentId, widget.pageId, signature, box);
      if (mounted) {
        setState(() => _signatures = [for (final item in _signatures) item.id == updated.id ? updated : item]);
      }
    });
  }

  void _delete(PageSignature signature) => _run(() async {
        await _edits.deleteSignature(widget.documentId, widget.pageId, signature);
        if (mounted) {
          setState(() {
            _signatures = _signatures.where((item) => item.id != signature.id).toList();
            _selectedId = null;
          });
        }
      });

  @override
  Widget build(BuildContext context) {
    final page = _page;
    final l = context.l;
    final selected = _signatures.where((item) => item.id == _selectedId).firstOrNull;
    return Scaffold(
      appBar: AppBar(title: Text(l.toolSign), actions: [
        if (selected != null)
          IconButton(tooltip: l.delete, onPressed: _busy ? null : () => _delete(selected), icon: const Icon(Icons.delete_outline)),
      ]),
      body: _error != null
          ? Center(child: Text(friendlyError(context, _error!)))
          : page == null
              ? const Center(child: CircularProgressIndicator())
              : Column(children: [
                  if (_busy) const LinearProgressIndicator(),
                  Padding(padding: const EdgeInsets.all(8), child: Text(l.signHint, textAlign: TextAlign.center)),
                  Expanded(
                    child: PageStage(
                      image: MemoryImage(page.bytes),
                      imageSize: page.size,
                      onTapPage: (point) {
                        final hit = _signatures.reversed.where((item) => point.dx >= item.box.x &&
                            point.dx <= item.box.right && point.dy >= item.box.y && point.dy <= item.box.bottom).firstOrNull;
                        setState(() => _selectedId = hit?.id);
                      },
                      builder: (context, rect) => [
                        for (final signature in _signatures)
                          if (signature.id == _selectedId)
                            DraggableBox(
                              pageRect: rect,
                              box: _dragBox ?? signature.box,
                              keepAspect: signature.aspectRatio,
                              onChanged: (box) => setState(() => _dragBox = box),
                              onChangeEnd: () => _saveMove(signature),
                              child: _image(signature),
                            )
                          else
                            Positioned.fromRect(
                              rect: PageStage.toRect(rect, signature.box),
                              child: IgnorePointer(child: _image(signature)),
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
                child: Row(children: [
                  Expanded(
                    child: FilledButton.icon(
                      onPressed: _busy ? null : () => _add(_draw),
                      icon: const Icon(Icons.draw_outlined),
                      label: Text(l.drawSignature),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: _busy ? null : () => _add(_photo),
                      icon: const Icon(Icons.photo_outlined),
                      label: Text(l.signaturePhoto),
                    ),
                  ),
                ]),
              ),
            ),
    );
  }

  Widget _image(PageSignature signature) {
    final bytes = _images[signature.id];
    return bytes == null ? const SizedBox() : Image.memory(bytes, fit: BoxFit.fill);
  }
}

/// Full-screen pad: sign with a finger, then Done returns a transparent PNG trimmed to the ink.
class SignaturePad extends StatefulWidget {
  const SignaturePad({super.key});

  @override
  State<SignaturePad> createState() => _SignaturePadState();
}

class _SignaturePadState extends State<SignaturePad> {
  final _strokes = <List<Offset>>[];
  Size _size = Size.zero;

  Future<Uint8List?> _export() async {
    final points = _strokes.expand((stroke) => stroke).toList();
    if (points.isEmpty) return null;
    const pen = 4.0, scale = 3.0;
    final bounds = points.skip(1).fold(Rect.fromLTWH(points.first.dx, points.first.dy, 0, 0),
        (rect, point) => rect.expandToInclude(Rect.fromLTWH(point.dx, point.dy, 0, 0))).inflate(pen * 2);
    final recorder = ui.PictureRecorder();
    final canvas = Canvas(recorder)
      ..scale(scale)
      ..translate(-bounds.left, -bounds.top);
    _SignaturePainter(_strokes, pen).paint(canvas, _size);
    final image = await recorder.endRecording()
        .toImage((bounds.width * scale).ceil(), (bounds.height * scale).ceil());
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    return data?.buffer.asUint8List();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(context.l.drawSignature), actions: [
          TextButton(onPressed: () => setState(_strokes.clear), child: Text(context.l.clear)),
          TextButton(
            onPressed: () async {
              final png = await _export();
              if (context.mounted) Navigator.pop(context, png);
            },
            child: Text(context.l.done),
          ),
        ]),
        body: LayoutBuilder(builder: (context, constraints) {
          _size = constraints.biggest;
          return GestureDetector(
            onPanStart: (details) => setState(() => _strokes.add([details.localPosition])),
            onPanUpdate: (details) => setState(() => _strokes.last.add(details.localPosition)),
            child: Container(
              color: Colors.white,
              child: CustomPaint(painter: _SignaturePainter(_strokes, 4), size: Size.infinite),
            ),
          );
        }),
      );
}

class _SignaturePainter extends CustomPainter {
  _SignaturePainter(this.strokes, this.pen);

  final List<List<Offset>> strokes;
  final double pen;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = const Color(0xFF0D2A6B)
      ..strokeWidth = pen
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round
      ..style = PaintingStyle.stroke;
    for (final stroke in strokes) {
      if (stroke.length == 1) {
        canvas.drawCircle(stroke.first, pen / 2, paint..style = PaintingStyle.fill);
        paint.style = PaintingStyle.stroke;
      } else {
        canvas.drawPath(Path()..addPolygon(stroke, false), paint);
      }
    }
  }

  @override
  bool shouldRepaint(_SignaturePainter old) => true;
}
