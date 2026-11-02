import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../models/complaint_model.dart';
import '../../providers/auth_provider.dart';
import '../../providers/complaint_provider.dart';
import '../../utils/constants.dart';
import '../../utils/responsive.dart';
import '../../widgets/complaint_card.dart';
import '../../widgets/skeleton.dart';
import '../../widgets/status_badge.dart';
import '../../widgets/visuals.dart';
import '../complaints/complaint_detail_screen.dart';
import '../complaints/rate_repair_screen.dart';
import '../complaints/submit_complaint_screen.dart';

class DashboardScreen extends StatelessWidget {
  /// Switches the bottom-navigation tab (0 dashboard, 1 history, 2 notifications, 3 profile).
  final ValueChanged<int> onNavigate;
  const DashboardScreen({super.key, required this.onNavigate});

  static String _greeting() {
    final h = DateTime.now().hour;
    if (h < 12) return 'Good morning';
    if (h < 17) return 'Good afternoon';
    return 'Good evening';
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final cp = context.watch<ComplaintProvider>();
    final student = auth.student;
    final current = cp.currentRequest;
    final toRate = cp.toRate;

    void open(ComplaintModel c) =>
        openPage(context, ComplaintDetailScreen(complaint: c));

    return Scaffold(
      backgroundColor: kBg,
      body: RefreshIndicator(
        color: kGreen,
        onRefresh: () => cp.loadComplaints(),
        child: CustomScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(child: _Hero(
              showActions: context.screenSize == ScreenSize.phone,
              loaded: cp.complaintsLoaded,
              greeting: '${_greeting()}, ${student?.firstName ?? ''}',
              room: student?.roomNumber ?? '',
              initials: student?.initials ?? '?',
              unread: cp.unreadNotifications,
              stats: [
                _StatData(Icons.hourglass_top_rounded, cp.pendingCount, 'Submitted', kBlue),
                _StatData(Icons.build_circle_outlined, cp.activeCount, 'In progress', kAmber),
                _StatData(Icons.check_circle_outline, cp.resolvedCount, 'Resolved', kGreenDark),
                _StatData(Icons.block_outlined, cp.rejectedCount, 'Rejected', kRed),
              ],
              onBell: () => onNavigate(2),
              onAvatar: () => onNavigate(3),
              onStats: () => onNavigate(1),
            )),
            SliverToBoxAdapter(
              child: CenteredContent(
                maxWidth: 1180,
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 8, 20, 32),
                  child: LayoutBuilder(builder: (context, box) {
                    final w = box.maxWidth;
                    final wide = w >= 900;
                    final medium = !wide && w >= 600;

                    final actions = Row(children: [
                      Expanded(flex: 3, child: _ActionButton(
                        icon: Icons.add_circle_outline,
                        label: 'Report an issue',
                        filled: true,
                        onTap: () => openPage(context, const SubmitComplaintScreen()),
                      )),
                      const SizedBox(width: 12),
                      Expanded(flex: 2, child: _ActionButton(
                        icon: Icons.warning_amber_rounded,
                        label: 'Emergency',
                        filled: false,
                        color: kRed,
                        onTap: () => openPage(context, const SubmitComplaintScreen(startAsEmergency: true)),
                      )),
                    ]);

                    Widget recent(int count, int columns) {
                      final items = cp.complaints.take(count).toList();
                      Widget body;
                      if (!cp.complaintsLoaded) {
                        body = Shimmer(
                          child: columns == 1
                              ? const Column(children: [SkeletonComplaintCard(), SkeletonComplaintCard(), SkeletonComplaintCard()])
                              : Wrap(
                                  spacing: 14,
                                  children: List.generate(4, (_) => SizedBox(width: (w - 14) / 2, child: const SkeletonComplaintCard())),
                                ),
                        );
                      } else if (cp.complaints.isEmpty) {
                        body = _EmptyState(
                          error: cp.error,
                          onRetry: () => cp.loadComplaints(),
                          onReport: () => openPage(context, const SubmitComplaintScreen()),
                        );
                      } else if (columns == 1) {
                        body = Column(children: items.map((c) => ComplaintCard(complaint: c, onTap: () => open(c))).toList());
                      } else {
                        // Fixed row height keeps both columns aligned.
                        body = GridView.builder(
                          shrinkWrap: true,
                          physics: const NeverScrollableScrollPhysics(),
                          padding: EdgeInsets.zero,
                          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                            crossAxisCount: 2, crossAxisSpacing: 14, mainAxisExtent: 130,
                          ),
                          itemCount: items.length,
                          itemBuilder: (_, i) => ComplaintCard(complaint: items[i], onTap: () => open(items[i])),
                        );
                      }
                      return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        _SectionTitle('Recent requests',
                            actionLabel: cp.complaints.length > count ? 'See all' : null, onAction: () => onNavigate(1)),
                        const SizedBox(height: 12),
                        body,
                      ]);
                    }

                    final rate = toRate == null
                        ? null
                        : _RatePrompt(
                            complaint: toRate,
                            onTap: () => openPage(context, RateRepairScreen(complaint: toRate), width: 560),
                          );
                    final currentBlock = !cp.complaintsLoaded
                        ? const Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                            Shimmer(child: SkeletonTitle(width: 150)),
                            SizedBox(height: 12),
                            Shimmer(child: SkeletonCurrentRequest()),
                          ])
                        : current == null
                        ? null
                        : Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                            const _SectionTitle('Current request'),
                            const SizedBox(height: 12),
                            _CurrentRequest(complaint: current, onTap: () => open(current)),
                          ]);

                    final lead = <Widget>[
                      actions,
                      if (rate != null) ...[const SizedBox(height: 20), rate],
                      if (currentBlock != null) ...[const SizedBox(height: 26), currentBlock],
                    ];

                    if (wide) {
                      // Desktop: actions and current request on the left, recent requests on the right.
                      return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Expanded(flex: 5, child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: lead)),
                        const SizedBox(width: 28),
                        Expanded(flex: 4, child: recent(6, 1)),
                      ]);
                    }
                    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      ...lead,
                      const SizedBox(height: 26),
                      recent(medium ? 6 : 4, medium ? 2 : 1),
                    ]);
                  }),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ───────────────────────── header + summary ─────────────────────────

class _StatData {
  final IconData icon;
  final int value;
  final String label;
  final Color color;
  const _StatData(this.icon, this.value, this.label, this.color);
}

class _Hero extends StatelessWidget {
  final String greeting, room, initials;
  final int unread;
  final List<_StatData> stats;
  final VoidCallback onBell, onAvatar, onStats;
  final bool showActions;
  final bool loaded;
  const _Hero({
    required this.showActions,
    required this.loaded,
    required this.greeting, required this.room, required this.initials, required this.unread,
    required this.stats, required this.onBell, required this.onAvatar, required this.onStats,
  });

  @override
  Widget build(BuildContext context) {
    final top = MediaQuery.of(context).padding.top;
    return Column(children: [
      Stack(clipBehavior: Clip.none, children: [
        Container(
          width: double.infinity,
          padding: EdgeInsets.fromLTRB(0, top + 18, 0, 74),
          decoration: const BoxDecoration(
            gradient: LinearGradient(begin: Alignment.topLeft, end: Alignment.bottomRight, colors: [kGreen, kGreenDark]),
            borderRadius: BorderRadius.vertical(bottom: Radius.circular(30)),
          ),
          child: CenteredContent(maxWidth: 1180, child: Padding(padding: const EdgeInsets.symmetric(horizontal: 20), child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(DateFormat('EEEE, d MMMM').format(DateTime.now()),
                    style: const TextStyle(color: Colors.white70, fontSize: 13, fontWeight: FontWeight.w500)),
                const SizedBox(height: 6),
                Text(greeting,
                    maxLines: 2, overflow: TextOverflow.ellipsis,
                    style: const TextStyle(color: Colors.white, fontSize: 25, fontWeight: FontWeight.w800, height: 1.15)),
                const SizedBox(height: 12),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                  decoration: BoxDecoration(color: Colors.white.withOpacity(.2), borderRadius: BorderRadius.circular(20)),
                  child: Row(mainAxisSize: MainAxisSize.min, children: [
                    const Icon(Icons.meeting_room_outlined, color: Colors.white, size: 15),
                    const SizedBox(width: 6),
                    Text('Room $room', style: const TextStyle(color: Colors.white, fontSize: 13, fontWeight: FontWeight.w600)),
                  ]),
                ),
              ]),
            ),
            if (showActions) ...[
            const SizedBox(width: 12),
            Column(children: [
              GestureDetector(
                onTap: onAvatar,
                child: Container(
                  width: 46, height: 46, alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: Colors.white, shape: BoxShape.circle,
                    boxShadow: const [BoxShadow(color: Color(0x33000000), blurRadius: 10, offset: Offset(0, 3))],
                  ),
                  child: Text(initials, style: const TextStyle(color: kGreenDark, fontWeight: FontWeight.w800, fontSize: 16)),
                ),
              ),
              const SizedBox(height: 10),
              InkWell(
                onTap: onBell,
                borderRadius: BorderRadius.circular(20),
                child: Container(
                  width: 40, height: 40,
                  decoration: BoxDecoration(color: Colors.white.withOpacity(.2), shape: BoxShape.circle),
                  child: Badge(
                    isLabelVisible: unread > 0,
                    label: Text('$unread'),
                    child: const Icon(Icons.notifications_none_rounded, color: Colors.white, size: 22),
                  ),
                ),
              ),
            ]),
            ],
          ]))),
        ),
        // Summary card overlapping the header
        Positioned(
          left: 0, right: 0, bottom: -46,
          child: CenteredContent(maxWidth: 1180, child: Padding(padding: const EdgeInsets.symmetric(horizontal: 20), child: Container(
            padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 6),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(22), boxShadow: kCardShadow),
            child: InkWell(
              borderRadius: BorderRadius.circular(22),
              onTap: onStats,
              child: Row(children: [
                for (var i = 0; i < stats.length; i++) ...[
                  Expanded(child: loaded ? _StatTile(stats[i]) : const Shimmer(child: SkeletonStatTile())),
                  if (i < stats.length - 1) Container(width: 1, height: 40, color: kBorder),
                ],
              ]),
            ),
          ))),
        ),
      ]),
      const SizedBox(height: 62),
    ]);
  }
}

class _StatTile extends StatelessWidget {
  final _StatData d;
  const _StatTile(this.d);
  @override
  Widget build(BuildContext context) => Column(mainAxisSize: MainAxisSize.min, children: [
        Icon(d.icon, color: d.color, size: 22),
        const SizedBox(height: 6),
        Text('${d.value}', style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: kText, height: 1)),
        const SizedBox(height: 4),
        Text(d.label, maxLines: 1, overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 11, color: kMuted, fontWeight: FontWeight.w600)),
      ]);
}

// ───────────────────────── sections ─────────────────────────

class _SectionTitle extends StatelessWidget {
  final String text;
  final String? actionLabel;
  final VoidCallback? onAction;
  const _SectionTitle(this.text, {this.actionLabel, this.onAction});
  @override
  Widget build(BuildContext context) => Row(children: [
        Text(text, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: kText)),
        const Spacer(),
        if (actionLabel != null)
          GestureDetector(
            onTap: onAction,
            child: Text(actionLabel!, style: const TextStyle(color: kGreenDark, fontWeight: FontWeight.w700, fontSize: 14)),
          ),
      ]);
}

class _ActionButton extends StatelessWidget {
  final IconData icon;
  final String label;
  final bool filled;
  final Color color;
  final VoidCallback onTap;
  const _ActionButton({required this.icon, required this.label, required this.filled, required this.onTap, this.color = kGreen});

  @override
  Widget build(BuildContext context) {
    final fg = filled ? Colors.white : color;
    return Material(
      color: filled ? color : Colors.white,
      borderRadius: BorderRadius.circular(16),
      child: InkWell(
        borderRadius: BorderRadius.circular(16),
        onTap: onTap,
        child: Container(
          height: 56,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(16),
            border: filled ? null : Border.all(color: color.withOpacity(.5), width: 1.5),
            boxShadow: filled ? [BoxShadow(color: color.withOpacity(.35), blurRadius: 14, offset: const Offset(0, 6))] : null,
          ),
          child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            Icon(icon, color: fg, size: 21),
            const SizedBox(width: 8),
            Flexible(child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis,
                style: TextStyle(color: fg, fontWeight: FontWeight.w800, fontSize: 15))),
          ]),
        ),
      ),
    );
  }
}

class _RatePrompt extends StatelessWidget {
  final ComplaintModel complaint;
  final VoidCallback onTap;
  const _RatePrompt({required this.complaint, required this.onTap});
  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: kAmberSoft, borderRadius: BorderRadius.circular(18),
          border: Border.all(color: kAmber.withOpacity(.35)),
        ),
        child: Row(children: [
          Container(
            width: 44, height: 44,
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14)),
            child: const Icon(Icons.star_rounded, color: kAmber, size: 26),
          ),
          const SizedBox(width: 12),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('How was the repair?', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 15, color: kText)),
            const SizedBox(height: 2),
            Text('${complaint.jobCode} is resolved. Tell us how it went.',
                style: const TextStyle(fontSize: 13, color: kMuted, height: 1.3)),
          ])),
          const SizedBox(width: 8),
          ElevatedButton(
            onPressed: onTap,
            style: ElevatedButton.styleFrom(minimumSize: const Size(72, 40), padding: const EdgeInsets.symmetric(horizontal: 16)),
            child: const Text('Rate'),
          ),
        ]),
      );
}

class _CurrentRequest extends StatelessWidget {
  final ComplaintModel complaint;
  final VoidCallback onTap;
  const _CurrentRequest({required this.complaint, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final c = complaint;
    final inProgress = c.statusId == 2;
    final progress = inProgress ? 0.62 : 0.22;
    final line = inProgress
        ? (c.workerName != null ? 'Assigned to ${c.workerName}' : 'Being handled by the maintenance team')
        : 'Waiting to be assigned to a technician';

    return Container(
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(20), boxShadow: kCardShadow, border: Border.all(color: kBorder)),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(20),
        child: InkWell(
          borderRadius: BorderRadius.circular(20),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Container(
                  width: 42, height: 42,
                  decoration: BoxDecoration(color: statusSoftColor(c.statusId), borderRadius: BorderRadius.circular(13)),
                  child: Icon(categoryIcon(c.categoryName), color: statusColor(c.statusId), size: 22),
                ),
                const SizedBox(width: 12),
                Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(c.categoryName, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15, color: kText)),
                  Text(c.jobCode, style: const TextStyle(fontSize: 12, color: kMuted, fontWeight: FontWeight.w600)),
                ])),
                StatusBadge(statusId: c.statusId, statusName: c.statusName),
              ]),
              const SizedBox(height: 14),
              Text(c.description, maxLines: 2, overflow: TextOverflow.ellipsis,
                  style: const TextStyle(color: kText, fontSize: 14, height: 1.4)),
              const SizedBox(height: 16),
              ClipRRect(
                borderRadius: BorderRadius.circular(6),
                child: LinearProgressIndicator(
                  value: progress, minHeight: 7,
                  backgroundColor: kBorder,
                  valueColor: AlwaysStoppedAnimation(statusColor(c.statusId)),
                ),
              ),
              const SizedBox(height: 10),
              Row(children: [
                Expanded(child: Text(line, style: const TextStyle(fontSize: 13, color: kMuted, fontWeight: FontWeight.w500))),
                if (inProgress && c.deadline != null)
                  Text(c.timerLabel, style: TextStyle(fontSize: 13, fontWeight: FontWeight.w800, color: c.isExpired ? kRed : kText)),
              ]),
            ]),
          ),
        ),
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  final String? error;
  final VoidCallback onRetry, onReport;
  const _EmptyState({required this.error, required this.onRetry, required this.onReport});
  @override
  Widget build(BuildContext context) {
    final failed = error != null;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(vertical: 34, horizontal: 24),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(20), border: Border.all(color: kBorder)),
      child: Column(children: [
        Container(
          width: 64, height: 64,
          decoration: BoxDecoration(color: failed ? const Color(0xFFFDECEC) : kGreenSoft, shape: BoxShape.circle),
          child: Icon(failed ? Icons.cloud_off_outlined : Icons.home_repair_service_outlined, color: failed ? kRed : kGreenDark, size: 30),
        ),
        const SizedBox(height: 14),
        Text(failed ? "Couldn't load your requests" : 'No requests yet',
            style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16, color: kText)),
        const SizedBox(height: 6),
        Text(failed ? error! : 'When something in your room needs fixing, report it here and track it from start to finish.',
            textAlign: TextAlign.center, style: const TextStyle(color: kMuted, fontSize: 14, height: 1.4)),
        const SizedBox(height: 18),
        failed
            ? OutlinedButton(onPressed: onRetry, child: const Text('Try again'))
            : ElevatedButton(onPressed: onReport, style: ElevatedButton.styleFrom(minimumSize: const Size(180, 46)), child: const Text('Report an issue')),
      ]),
    );
  }
}
