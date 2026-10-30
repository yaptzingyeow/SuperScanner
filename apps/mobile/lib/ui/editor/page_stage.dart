import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../../api/api_client.dart';
import '../../api/edit_api.dart';
import '../../api/models.dart';

/// The current page image and its pixel size, loaded fresh (edits change it).
class PageImage {
  const PageImage(this.bytes, this.size, this.page);

  final Uint8List bytes;
  final Size size;
  final DocumentPage page;

  static Future<PageImage> load(DocumentsApi api, String documentId, String pageId) async {
    final document = await api.getDocument(documentId);
    final page = document.pages.firstWhere((item) => item.id == pageId);
    final bytes = await api.pageImage(documentId, pageId, 'preview');
    final image = await decodeImage(bytes);
    return PageImage(bytes, Size(image.width.toDouble(), image.height.toDouble()), page);
  }

  static Future<ui.Image> decodeImage(Uint8List bytes) async {
    final codec = await ui.instantiateImageCodec(bytes);
    return (await codec.getNextFrame()).image;
  }
}

/// Shows a page image fitted to the available space and lets tools draw on top of it.
/// [builder] gets the image's rectangle on screen so normalized boxes can be placed with [toRect].
class PageStage extends StatelessWidget {
  const PageStage({super.key, required this.image, required this.imageSize, this.builder, this.onTapPage,
      this.onPanStart, this.onPanUpdate, this.onPanEnd});

  final ImageProvider image;
  final Size imageSize;
  final List<Widget> Function(BuildContext context, Rect rect)? builder;

  /// Tap position, normalized 0..1 on the page.
  final void Function(Offset point)? onTapPage;
  final void Function(Offset point)? onPanStart;
  final void Function(Offset point)? onPanUpdate;
  final VoidCallback? onPanEnd;

  static Rect fit(Size box, Size image) {
    final scale = (box.width / image.width) < (box.height / image.height)
        ? box.width / image.width
        : box.height / image.height;
    final size = Size(image.width * scale, image.height * scale);
    return Offset((box.width - size.width) / 2, (box.height - size.height) / 2) & size;
  }

  static Rect toRect(Rect page, PageBox box) =>
      Rect.fromLTWH(page.left + box.x * page.width, page.top + box.y * page.height, box.width * page.width,
          box.height * page.height);

  @override
  Widget build(BuildContext context) => LayoutBuilder(builder: (context, constraints) {
        final rect = fit(constraints.biggest, imageSize);
        Offset normalize(Offset local) => Offset(((local.dx - rect.left) / rect.width).clamp(0.0, 1.0),
            ((local.dy - rect.top) / rect.height).clamp(0.0, 1.0));
        return GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTapUp: onTapPage == null ? null : (details) => onTapPage!(normalize(details.localPosition)),
          onPanStart: onPanStart == null ? null : (details) => onPanStart!(normalize(details.localPosition)),
          onPanUpdate: onPanUpdate == null ? null : (details) => onPanUpdate!(normalize(details.localPosition)),
          onPanEnd: onPanEnd == null ? null : (_) => onPanEnd!(),
          child: Stack(children: [
            Positioned.fromRect(rect: rect, child: Image(image: image, fit: BoxFit.fill, gaplessPlayback: true)),
            ...?builder?.call(context, rect),
          ]),
        );
      });
}

/// A box on the page the user can drag to move and resize from its corner handle.
class DraggableBox extends StatelessWidget {
  const DraggableBox({super.key, required this.pageRect, required this.box, required this.child,
      required this.onChanged, this.onChangeEnd, this.selected = true, this.keepAspect});

  final Rect pageRect;
  final PageBox box;
  final Widget child;
  final bool selected;

  /// Width / height in page pixels to keep while resizing (signatures and marks).
  final double? keepAspect;
  final ValueChanged<PageBox> onChanged;
  final VoidCallback? onChangeEnd;

  @override
  Widget build(BuildContext context) {
    final rect = PageStage.toRect(pageRect, box);
    return Positioned.fromRect(
      rect: rect.inflate(16),
      child: Stack(clipBehavior: Clip.none, children: [
        Positioned.fill(
          child: GestureDetector(
            onPanUpdate: (details) =>
                onChanged(box.moved(details.delta.dx / pageRect.width, details.delta.dy / pageRect.height)),
            onPanEnd: (_) => onChangeEnd?.call(),
            child: Container(
              margin: const EdgeInsets.all(16),
              decoration: selected
                  ? BoxDecoration(border: Border.all(color: Theme.of(context).colorScheme.primary, width: 2))
                  : null,
              child: child,
            ),
          ),
        ),
        if (selected)
          Positioned(
            right: 0,
            bottom: 0,
            child: GestureDetector(
              onPanUpdate: (details) {
                final width = box.width + details.delta.dx / pageRect.width;
                final height = keepAspect == null
                    ? box.height + details.delta.dy / pageRect.height
                    : width * pageRect.width / keepAspect! / pageRect.height;
                onChanged(PageBox(box.x, box.y, width, height).clamp());
              },
              onPanEnd: (_) => onChangeEnd?.call(),
              child: Container(
                width: 32,
                height: 32,
                decoration: BoxDecoration(color: Theme.of(context).colorScheme.primary, shape: BoxShape.circle),
                child: const Icon(Icons.open_in_full, size: 18, color: Colors.white),
              ),
            ),
          ),
      ]),
    );
  }
}
