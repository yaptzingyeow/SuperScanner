import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/api_client.dart';
import '../services/ad_service.dart';
import '../services/auth_service.dart';
import '../services/session_state.dart';
import 'common.dart';

/// Called when a Free account hits its daily OCR limit: offers a rewarded ad for bonus pages.
/// Returns true once the server has added the pages, so the caller can retry. Shows its own messages.
Future<bool> offerRewardedAd(BuildContext context) async {
  final l = context.l;
  final session = context.read<SessionState>();
  final documents = context.read<DocumentsApi>();
  final ads = context.read<AdService>();
  final uid = context.read<AuthService>().uid;
  await session.refresh();
  final plan = session.plan;
  if (!context.mounted) return false;
  if (plan == null || !plan.rewardedAds || uid == null) {
    showMessage(context, l.errLimit);
    return false;
  }

  final watch = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      icon: const Icon(Icons.ondemand_video_outlined),
      title: Text(l.adLimitTitle),
      content: Text('${l.adLimitBody(plan.rewardedOcrPages)}\n\n${l.adRewardsLeft(plan.rewardsLeftToday)}'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: Text(l.notNow)),
        FilledButton.icon(
          onPressed: () => Navigator.pop(context, true),
          icon: const Icon(Icons.play_arrow),
          label: Text(l.watchAd),
        ),
      ],
    ),
  );
  if (watch != true || !context.mounted) return false;

  try {
    final reward = await documents.startAdReward();
    if (!await ads.prepare()) {
      if (context.mounted) showMessage(context, l.adsUnavailable);
      return false;
    }
    final result = await ads.showRewarded(userId: uid, rewardId: reward.id);
    if (!context.mounted) return false;
    if (result != RewardedAdResult.earned) {
      showMessage(context, result == RewardedAdResult.skipped ? l.adNotFinished : l.adsUnavailable);
      return false;
    }
    final granted = plan.unverifiedAdClaim
        ? await documents.claimAdReward(reward.id).then((_) => true)
        : await _waitForGrant(documents, reward.id);
    await session.refresh();
    if (!context.mounted) return granted;
    showMessage(context, granted ? l.adRewardGranted(reward.pages) : l.adRewardPending);
    return granted;
  } catch (error) {
    if (context.mounted) showMessage(context, friendlyError(context, error));
    return false;
  }
}

/// AdMob's signed callback reaches our server separately, usually within a few seconds.
Future<bool> _waitForGrant(DocumentsApi documents, String id) async {
  for (var attempt = 0; attempt < 20; attempt++) {
    if (await documents.adRewardGranted(id)) return true;
    await Future<void>.delayed(const Duration(seconds: 1));
  }
  return false;
}
