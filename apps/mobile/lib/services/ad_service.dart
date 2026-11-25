import 'dart:async';
import 'dart:io';

import 'package:app_tracking_transparency/app_tracking_transparency.dart';
import 'package:google_mobile_ads/google_mobile_ads.dart';

import '../config.dart';

/// AdMob rewarded ads. Nothing loads until a Free user chooses to watch an ad, so consent and
/// Apple's tracking prompt only appear for people who use ads.
class AdService {
  bool _ready = false;

  /// Asks for ad consent where the law requires it (Google's form, EEA/UK), then Apple's tracking
  /// permission, then starts the ads SDK. Returns false when ads can't be requested.
  Future<bool> prepare() async {
    if (_ready) return true;
    final updated = Completer<void>();
    ConsentInformation.instance.requestConsentInfoUpdate(
      ConsentRequestParameters(),
      () => updated.complete(),
      (_) => updated.complete(), // Offline or misconfigured: fall back to the cached consent below.
    );
    await updated.future;
    final dismissed = Completer<void>();
    await ConsentForm.loadAndShowConsentFormIfRequired((_) => dismissed.complete());
    await dismissed.future;
    if (!await ConsentInformation.instance.canRequestAds()) return false;

    if (Platform.isIOS && await AppTrackingTransparency.trackingAuthorizationStatus == TrackingStatus.notDetermined) {
      // A refusal still allows non-personalised ads.
      await AppTrackingTransparency.requestTrackingAuthorization();
    }
    await MobileAds.instance.initialize();
    return _ready = true;
  }

  /// Loads and shows a rewarded ad. [rewardId] travels in AdMob's signed callback to our server, which
  /// grants the pages; the result only says whether the viewer earned the reward on the phone.
  Future<RewardedAdResult> showRewarded({required String userId, required String rewardId}) async {
    final loaded = Completer<RewardedAd?>();
    await RewardedAd.load(
      adUnitId: AppConfig.admobRewardedUnit,
      request: const AdRequest(),
      rewardedAdLoadCallback: RewardedAdLoadCallback(
        onAdLoaded: loaded.complete,
        onAdFailedToLoad: (_) => loaded.complete(null),
      ),
    );
    final ad = await loaded.future;
    if (ad == null) return RewardedAdResult.unavailable;

    await ad.setServerSideOptions(ServerSideVerificationOptions(userId: userId, customData: rewardId));
    final closed = Completer<void>();
    var earned = false;
    ad.fullScreenContentCallback = FullScreenContentCallback(
      onAdDismissedFullScreenContent: (ad) {
        ad.dispose();
        if (!closed.isCompleted) closed.complete();
      },
      onAdFailedToShowFullScreenContent: (ad, _) {
        ad.dispose();
        if (!closed.isCompleted) closed.complete();
      },
    );
    await ad.show(onUserEarnedReward: (_, _) => earned = true);
    await closed.future;
    return earned ? RewardedAdResult.earned : RewardedAdResult.skipped;
  }
}

enum RewardedAdResult { earned, skipped, unavailable }
