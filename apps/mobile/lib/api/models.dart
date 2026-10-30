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
