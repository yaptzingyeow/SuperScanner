/// Data shapes returned by the Arks Scanner API (same as apps/web/src/app/documents/document.models.ts).
library;

class DocumentSummary {
  const DocumentSummary({required this.id, required this.title, required this.status,
      required this.pageCount, required this.updatedAt});

  final String id;
  final String title;
  final String status;
  final int pageCount;
  final DateTime updatedAt;

  factory DocumentSummary.fromJson(Map<String, dynamic> json) => DocumentSummary(
        id: json['id'] as String,
        title: json['title'] as String,
        status: json['status'] as String,
        pageCount: (json['pageCount'] as num).toInt(),
        updatedAt: DateTime.parse(json['updatedAt'] as String),
      );
}

class DocumentPage {
  const DocumentPage({required this.id, required this.position, required this.pageNumber, required this.state,
      required this.hasPreview, required this.canCrop, required this.cropStatus, required this.previewRevision,
      this.failureCode, this.filter, this.appliedFilter});

  final String id;
  final int position;
  final int pageNumber;
  final String state;
  final String? failureCode;
  final bool hasPreview;
  final bool canCrop;
  final String cropStatus;
  final String previewRevision;
  final String? filter;
  final String? appliedFilter;

  bool get isFailed => state == 'Failed';

  /// Ready to show: the page has a preview and no crop/look is still being applied.
  bool get isSettled =>
      hasPreview && !isFailed && state != 'Importing' && state != 'Processing' && !_busyCrop.contains(cropStatus);

  static const _busyCrop = {'Detecting', 'Processing'};

  bool get isProcessing => state == 'Importing' || state == 'Processing' || _busyCrop.contains(cropStatus);

  factory DocumentPage.fromJson(Map<String, dynamic> json) => DocumentPage(
        id: json['id'] as String,
        position: (json['position'] as num).toInt(),
        pageNumber: (json['pageNumber'] as num).toInt(),
        state: json['state'] as String,
        failureCode: json['failureCode'] as String?,
        hasPreview: json['hasPreview'] as bool? ?? false,
        canCrop: json['canCrop'] as bool? ?? false,
        cropStatus: json['cropStatus'] as String? ?? '',
        previewRevision: '${json['previewRevision'] ?? ''}',
        filter: json['filter'] as String?,
        appliedFilter: json['appliedFilter'] as String?,
      );
}

class DocumentImport {
  const DocumentImport({required this.uploadId, required this.state, required this.discoveredPageCount,
      required this.createdPageCount, required this.failedPageCount, this.errorCode});

  final String uploadId;
  final String state;
  final int discoveredPageCount;
  final int createdPageCount;
  final int failedPageCount;
  final String? errorCode;

  /// The document endpoint reports a fully expanded import as "Completed" and an expansion error as "Failed".
  bool get isFinished =>
      state == 'Completed' || state == 'Failed' || state == 'Rejected' || errorCode != null ||
      (state == 'Accepted' && discoveredPageCount > 0 && createdPageCount + failedPageCount == discoveredPageCount);

  factory DocumentImport.fromJson(Map<String, dynamic> json) => DocumentImport(
        uploadId: json['uploadId'] as String,
        state: json['state'] as String,
        discoveredPageCount: (json['discoveredPageCount'] as num?)?.toInt() ?? 0,
        createdPageCount: (json['createdPageCount'] as num?)?.toInt() ?? 0,
        failedPageCount: (json['failedPageCount'] as num?)?.toInt() ?? 0,
        errorCode: json['errorCode'] as String?,
      );
}

class DocumentDetail {
  const DocumentDetail({required this.id, required this.title, required this.status, required this.revision,
      required this.pageOrderRevision, required this.pages, required this.imports, this.message});

  final String id;
  final String title;
  final String status;
  final String? message;
  final int revision;
  final int pageOrderRevision;
  final List<DocumentPage> pages;
  final List<DocumentImport> imports;

  /// Still importing or processing: keep polling.
  /// The server is still working on it. A page waiting for the user (NeedsCrop) does not count: export leaves it out.
  bool get isBusy => imports.any((item) => !item.isFinished) || pages.any((page) => page.isProcessing);

  factory DocumentDetail.fromJson(Map<String, dynamic> json) => DocumentDetail(
        id: json['id'] as String,
        title: json['title'] as String,
        status: json['status'] as String,
        message: json['message'] as String?,
        revision: (json['revision'] as num?)?.toInt() ?? 0,
        pageOrderRevision: (json['pageOrderRevision'] as num?)?.toInt() ?? 0,
        pages: [
          for (final page in (json['pages'] as List? ?? const []))
            DocumentPage.fromJson(page as Map<String, dynamic>),
        ]..sort((a, b) => a.position.compareTo(b.position)),
        imports: [
          for (final item in (json['imports'] as List? ?? const []))
            DocumentImport.fromJson(item as Map<String, dynamic>),
        ],
      );
}

class UploadIntent {
  const UploadIntent({required this.uploadId, required this.putUrl, required this.expiresAt});

  final String uploadId;
  final Uri putUrl;
  final DateTime expiresAt;

  factory UploadIntent.fromJson(Map<String, dynamic> json) => UploadIntent(
        uploadId: json['uploadId'] as String,
        putUrl: Uri.parse(json['putUrl'] as String),
        expiresAt: DateTime.parse(json['expiresAt'] as String),
      );
}

class UploadStatus {
  const UploadStatus({required this.state, required this.discoveredPageCount, required this.createdPageCount,
      required this.failedPageCount, this.errorCode});

  final String state;
  final int discoveredPageCount;
  final int createdPageCount;
  final int failedPageCount;
  final String? errorCode;

  bool get isRejected => state == 'Rejected' || errorCode != null;
  bool get isComplete => state == 'Accepted' && discoveredPageCount > 0 &&
      createdPageCount + failedPageCount == discoveredPageCount;

  factory UploadStatus.fromJson(Map<String, dynamic> json) => UploadStatus(
        state: json['state'] as String,
        discoveredPageCount: (json['discoveredPageCount'] as num?)?.toInt() ?? 0,
        createdPageCount: (json['createdPageCount'] as num?)?.toInt() ?? 0,
        failedPageCount: (json['failedPageCount'] as num?)?.toInt() ?? 0,
        errorCode: json['errorCode'] as String?,
      );
}

class CropPoint {
  const CropPoint(this.x, this.y);

  final double x;
  final double y;

  Map<String, dynamic> toJson() => {'x': x, 'y': y};

  factory CropPoint.fromJson(Map<String, dynamic> json) =>
      CropPoint((json['x'] as num).toDouble(), (json['y'] as num).toDouble());
}

class CropState {
  const CropState({required this.revision, required this.status, required this.filter,
      required this.rotation, this.points});

  final int revision;
  final String status;
  final String filter;
  final int rotation;
  final List<CropPoint>? points;

  factory CropState.fromJson(Map<String, dynamic> json) => CropState(
        revision: (json['revision'] as num).toInt(),
        status: json['status'] as String? ?? '',
        filter: json['filter'] as String? ?? 'Magic',
        rotation: (json['rotation'] as num?)?.toInt() ?? 0,
        points: (json['points'] as List?)
            ?.map((point) => CropPoint.fromJson(point as Map<String, dynamic>))
            .toList(),
      );
}

class DocumentExport {
  const DocumentExport({required this.id, required this.state, this.failureCode});

  final String id;
  final String state;
  final String? failureCode;

  bool get isReady => state == 'Ready';
  bool get isFailed => state == 'Failed' || failureCode != null;

  factory DocumentExport.fromJson(Map<String, dynamic> json) => DocumentExport(
        id: json['id'] as String,
        state: json['state'] as String,
        failureCode: json['failureCode'] as String?,
      );
}

/// A watermark font the export worker has; [hasBold] says whether a bold face exists.
typedef WatermarkFont = ({String id, String label, bool hasBold});

/// The user's own watermark on every exported page (server: ExportWatermark), e.g. "FOR GOVERNMENT USE ONLY".
/// Defaults and limits match the server and the web export panel.
class WatermarkSettings {
  const WatermarkSettings({
    this.text = 'FOR GOVERNMENT USE ONLY',
    this.layout = 'Single',
    this.fontId = 'noto-sans',
    this.bold = true,
    this.color = '#C62828',
    this.opacity = .25,
    this.sizePercent = 8,
    this.angleDegrees = 35,
    this.spacing = 1.5,
    this.position = 'Center',
  });

  static const maxTextLength = 120;

  static const List<WatermarkFont> fonts = [
    (id: 'noto-sans', label: 'Noto Sans', hasBold: true),
    (id: 'liberation-sans', label: 'Arial style', hasBold: true),
    (id: 'carlito', label: 'Calibri style', hasBold: true),
    (id: 'poppins', label: 'Poppins', hasBold: true),
    (id: 'lato', label: 'Lato', hasBold: true),
    (id: 'oswald', label: 'Oswald (stamp)', hasBold: false),
    (id: 'montserrat', label: 'Montserrat', hasBold: false),
    (id: 'noto-serif', label: 'Noto Serif', hasBold: true),
    (id: 'liberation-serif', label: 'Times style', hasBold: true),
    (id: 'liberation-mono', label: 'Typewriter', hasBold: true),
    (id: 'caveat', label: 'Caveat (handwriting)', hasBold: false),
    (id: 'dancing-script', label: 'Dancing Script', hasBold: false),
  ];

  static const colors = ['#C62828', '#1A237E', '#212121', '#757575', '#2E7D32', '#EF6C00'];

  /// Single: once per page. Tiled: repeated across the page.
  final String text;
  final String layout;
  final String fontId;
  final bool bold;

  /// #RRGGBB
  final String color;

  /// 0.05 (very faint) to 1 (solid).
  final double opacity;

  /// Text height as % of the page's shorter side, 2–25.
  final double sizePercent;

  /// Counter-clockwise, -90 to 90.
  final double angleDegrees;

  /// Gap between repeated copies in text heights, 0.5–4 (Tiled only).
  final double spacing;

  /// Center, Top or Bottom (Single only).
  final String position;

  bool get isTiled => layout == 'Tiled';
  bool get fontHasBold => fonts.where((font) => font.id == fontId).firstOrNull?.hasBold ?? false;
  bool get isValid => text.trim().isNotEmpty && text.trim().length <= maxTextLength;

  WatermarkSettings copyWith({String? text, String? layout, String? fontId, bool? bold, String? color, double? opacity,
          double? sizePercent, double? angleDegrees, double? spacing, String? position}) =>
      WatermarkSettings(
        text: text ?? this.text,
        layout: layout ?? this.layout,
        fontId: fontId ?? this.fontId,
        bold: bold ?? this.bold,
        color: color ?? this.color,
        opacity: opacity ?? this.opacity,
        sizePercent: sizePercent ?? this.sizePercent,
        angleDegrees: angleDegrees ?? this.angleDegrees,
        spacing: spacing ?? this.spacing,
        position: position ?? this.position,
      );

  /// As the server expects it: trimmed text, bold only where the font has it, upper-case colour.
  Map<String, dynamic> toJson() => {
        'text': text.trim(),
        'layout': layout,
        'fontId': fontId,
        'bold': bold && fontHasBold,
        'color': color.toUpperCase(),
        'opacity': opacity,
        'sizePercent': sizePercent,
        'angleDegrees': angleDegrees,
        'spacing': spacing,
        'position': position,
      };

  /// Reads saved settings, falling back to defaults for anything missing or out of range.
  factory WatermarkSettings.fromJson(Map<String, dynamic> json) {
    const d = WatermarkSettings();
    double number(String key, double fallback, double min, double max) {
      final value = (json[key] as num?)?.toDouble();
      return value == null || !value.isFinite || value < min || value > max ? fallback : value;
    }

    String pick(String key, String fallback, Iterable<String> allowed) {
      final value = json[key];
      return value is String && allowed.contains(value) ? value : fallback;
    }

    final text = json['text'];
    return WatermarkSettings(
      text: text is String && text.trim().isNotEmpty && text.length <= maxTextLength ? text : d.text,
      layout: pick('layout', d.layout, const ['Single', 'Tiled']),
      fontId: pick('fontId', d.fontId, fonts.map((font) => font.id)),
      bold: json['bold'] is bool ? json['bold'] as bool : d.bold,
      color: json['color'] is String && RegExp(r'^#[0-9A-Fa-f]{6}$').hasMatch(json['color'] as String)
          ? json['color'] as String
          : d.color,
      opacity: number('opacity', d.opacity, .05, 1),
      sizePercent: number('sizePercent', d.sizePercent, 2, 25),
      angleDegrees: number('angleDegrees', d.angleDegrees, -90, 90),
      spacing: number('spacing', d.spacing, .5, 4),
      position: pick('position', d.position, const ['Center', 'Top', 'Bottom']),
    );
  }
}

class PlanInfo {
  const PlanInfo({required this.plan, this.consentVersion, this.currentPrivacyVersion});

  final String plan;
  final String? consentVersion;
  final String? currentPrivacyVersion;

  bool get isPro => plan == 'Pro';

  /// Same rule as the web: consent again whenever the privacy notice version changes.
  bool get needsConsent => currentPrivacyVersion != null && consentVersion != currentPrivacyVersion;

  factory PlanInfo.fromJson(Map<String, dynamic> json) => PlanInfo(
        plan: json['plan'] as String? ?? 'Free',
        consentVersion: json['privacyConsentVersion'] as String?,
        currentPrivacyVersion: json['currentPrivacyVersion'] as String?,
      );
}

/// The scan looks the server can apply, in filter-strip order (labels are translated in the UI).
const scanLooks = ['Magic', 'Original', 'Document', 'Bright', 'RemoveShadows', 'Grayscale', 'BlackAndWhite'];
