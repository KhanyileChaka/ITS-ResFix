import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../models/complaint_model.dart';
import '../../providers/complaint_provider.dart';
import '../../utils/constants.dart';
import '../../utils/responsive.dart';
import '../../widgets/complaint_card.dart';
import '../../widgets/skeleton.dart';
import '../../widgets/status_badge.dart';
import '../../widgets/ui.dart';
import '../../widgets/visuals.dart';
import '../complaints/complaint_detail_screen.dart';
import '../complaints/submit_complaint_screen.dart';

/// Layouts: phone = card list, tablet = two-column cards, desktop = table.
class HistoryScreen extends StatefulWidget {
  const HistoryScreen({super.key});
  @override
  State<HistoryScreen> createState() => _HistoryScreenState();
}

class _HistoryScreenState extends State<HistoryScreen> {
  int? _status; // null = all
  String _query = '';
  final _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  bool _matches(ComplaintModel c) {
    if (_status != null && c.statusId != _status) return false;
    final q = _query.trim().toLowerCase();
    if (q.isEmpty) return true;
    return c.description.toLowerCase().contains(q) ||
        c.categoryName.toLowerCase().contains(q) ||
        c.jobCode.toLowerCase().contains(q) ||
        c.statusName.toLowerCase().contains(q);
  }

  @override
  Widget build(BuildContext context) {
    final cp = context.watch<ComplaintProvider>();
    final all = cp.complaints;
    final filtered = all.where(_matches).toList();
    int count(int s) => all.where((c) => c.statusId == s).length;
    final wideHeader = context.screenSize != ScreenSize.phone;

    void open(ComplaintModel c) => openPage(context, ComplaintDetailScreen(complaint: c));
    void report() => openPage(context, const SubmitComplaintScreen());

    return Scaffold(
      backgroundColor: kBg,
      body: CenteredContent(
        maxWidth: 1280,
        child: Column(children: [
          PageHeader(
            title: 'My requests',
            subtitle: !cp.complaintsLoaded ? 'Loading your requests…' : '${all.length} ${all.length == 1 ? 'request' : 'requests'} in total',
            trailing: wideHeader
                ? ElevatedButton.icon(
                    onPressed: report,
                    icon: const Icon(Icons.add_rounded, size: 20),
                    label: const Text('New request'),
                    style: ElevatedButton.styleFrom(minimumSize: const Size(0, 44), padding: const EdgeInsets.symmetric(horizontal: 18)),
                  )
                : null,
            bottom: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              TextField(
                controller: _search,
                onChanged: (v) => setState(() => _query = v),
                textInputAction: TextInputAction.search,
                decoration: InputDecoration(
                  hintText: 'Search by description, category, status or code',
                  prefixIcon: const Icon(Icons.search_rounded, color: kMuted),
                  suffixIcon: _query.isEmpty
                      ? null
                      : IconButton(icon: const Icon(Icons.close_rounded, color: kMuted), onPressed: () { _search.clear(); setState(() => _query = ''); }),
                  contentPadding: const EdgeInsets.symmetric(vertical: 12),
                  fillColor: kBg,
                ),
              ),
              const SizedBox(height: 12),
              SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(children: [
                  FilterPill(label: 'All', count: cp.complaintsLoaded ? all.length : null, selected: _status == null, onTap: () => setState(() => _status = null)),
                  const SizedBox(width: 8),
                  FilterPill(label: 'Submitted', count: cp.complaintsLoaded ? count(1) : null, selected: _status == 1, onTap: () => setState(() => _status = 1)),
                  const SizedBox(width: 8),
                  FilterPill(label: 'In progress', count: cp.complaintsLoaded ? count(2) : null, selected: _status == 2, onTap: () => setState(() => _status = 2)),
                  const SizedBox(width: 8),
                  FilterPill(label: 'Resolved', count: cp.complaintsLoaded ? count(3) : null, selected: _status == 3, onTap: () => setState(() => _status = 3)),
                  const SizedBox(width: 8),
                  FilterPill(label: 'Rejected', count: cp.complaintsLoaded ? count(4) : null, selected: _status == 4, onTap: () => setState(() => _status = 4)),
                ]),
              ),
            ]),
          ),
          Expanded(
            child: RefreshIndicator(
              color: kGreen,
              onRefresh: () => cp.loadComplaints(),
              child: LayoutBuilder(builder: (context, box) {
                final table = box.maxWidth >= 1000;
                final twoCols = !table && box.maxWidth >= 640;

                if (!cp.complaintsLoaded) {
                  return ListView(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.all(20),
                    children: [
                      Shimmer(
                        child: table
                            ? const _TableSkeleton()
                            : twoCols
                                ? Wrap(spacing: 14, children: List.generate(8, (_) => SizedBox(width: (box.maxWidth - 54) / 2, child: const SkeletonComplaintCard())))
                                : Column(children: List.generate(6, (_) => const SkeletonComplaintCard())),
                      ),
                    ],
                  );
                }

                if (all.isEmpty) {
                  return ListView(physics: const AlwaysScrollableScrollPhysics(), children: [
                    SizedBox(
                      height: box.maxHeight,
                      child: StateMessage(
                        icon: cp.error != null ? Icons.cloud_off_outlined : Icons.inbox_outlined,
                        color: cp.error != null ? kRed : kGreenDark,
                        softColor: cp.error != null ? const Color(0xFFFDECEC) : kGreenSoft,
                        title: cp.error != null ? "Couldn't load your requests" : 'No requests yet',
                        message: cp.error ?? 'Everything you report will appear here so you can follow it from start to finish.',
                        actionLabel: cp.error != null ? 'Try again' : 'Report an issue',
                        onAction: cp.error != null ? () => cp.loadComplaints() : report,
                      ),
                    ),
                  ]);
                }

                if (filtered.isEmpty) {
                  return ListView(physics: const AlwaysScrollableScrollPhysics(), children: [
                    SizedBox(
                      height: box.maxHeight,
                      child: StateMessage(
                        icon: Icons.search_off_rounded,
                        color: kMuted,
                        softColor: const Color(0xFFEEF1F5),
                        title: 'No matches',
                        message: 'Nothing matches your search or filter. Try a different word or show all requests.',
                        actionLabel: 'Clear filters',
                        onAction: () { _search.clear(); setState(() { _query = ''; _status = null; }); },
                      ),
                    ),
                  ]);
                }

                if (table) {
                  return ListView(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.all(20),
                    children: [_RequestTable(items: filtered, onOpen: open)],
                  );
                }
                if (twoCols) {
                  return GridView.builder(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.all(20),
                    gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, crossAxisSpacing: 14, mainAxisExtent: 130),
                    itemCount: filtered.length,
                    itemBuilder: (_, i) => ComplaintCard(complaint: filtered[i], onTap: () => open(filtered[i])),
                  );
                }
                return ListView.builder(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.all(20),
                  itemCount: filtered.length,
                  itemBuilder: (_, i) => ComplaintCard(complaint: filtered[i], onTap: () => open(filtered[i])),
                );
              }),
            ),
          ),
        ]),
      ),
    );
  }
}

// ───────────────────────── desktop table ─────────────────────────

const _hStyle = TextStyle(fontSize: 11.5, fontWeight: FontWeight.w800, color: kMuted, letterSpacing: .7);

class _RequestTable extends StatelessWidget {
  final List<ComplaintModel> items;
  final ValueChanged<ComplaintModel> onOpen;
  const _RequestTable({required this.items, required this.onOpen});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), border: Border.all(color: kBorder), boxShadow: kCardShadow),
      clipBehavior: Clip.antiAlias,
      child: Column(children: [
        Container(
          color: const Color(0xFFF9FBFD),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 13),
          child: const Row(children: [
            Expanded(flex: 6, child: Text('REQUEST', style: _hStyle)),
            Expanded(flex: 3, child: Text('CATEGORY', style: _hStyle)),
            Expanded(flex: 2, child: Text('STATUS', style: _hStyle)),
            Expanded(flex: 2, child: Text('SENT', style: _hStyle)),
            SizedBox(width: 28),
          ]),
        ),
        const Divider(height: 1),
        for (var i = 0; i < items.length; i++) ...[
          _TableRow(c: items[i], onTap: () => onOpen(items[i])),
          if (i < items.length - 1) const Divider(height: 1, indent: 20, endIndent: 20),
        ],
      ]),
    );
  }
}

class _TableRow extends StatelessWidget {
  final ComplaintModel c;
  final VoidCallback onTap;
  const _TableRow({required this.c, required this.onTap});

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: onTap,
        hoverColor: kGreenSoft.withOpacity(.35),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
          child: Row(crossAxisAlignment: CrossAxisAlignment.center, children: [
            Expanded(
              flex: 6,
              child: Row(children: [
                Container(
                  width: 42, height: 42,
                  decoration: BoxDecoration(color: statusSoftColor(c.statusId), borderRadius: BorderRadius.circular(13)),
                  child: Icon(categoryIcon(c.categoryName), color: statusColor(c.statusId), size: 21),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(c.description, maxLines: 1, overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700, color: kText)),
                    const SizedBox(height: 3),
                    Row(children: [
                      Text(c.jobCode, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: kMuted)),
                      if (c.isEmergency) ...[
                        const SizedBox(width: 8),
                        const Text('EMERGENCY', style: TextStyle(fontSize: 11, fontWeight: FontWeight.w800, color: kRed)),
                      ],
                    ]),
                  ]),
                ),
                const SizedBox(width: 12),
              ]),
            ),
            Expanded(flex: 3, child: Text(c.categoryName, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 14, color: kText))),
            Expanded(flex: 2, child: Align(alignment: Alignment.centerLeft, child: StatusBadge(statusId: c.statusId, statusName: c.statusName))),
            Expanded(flex: 2, child: Text(DateFormat('d MMM yyyy').format(c.createdAt.toLocal()), style: const TextStyle(fontSize: 14, color: kMuted))),
            const SizedBox(width: 28, child: Icon(Icons.chevron_right_rounded, color: kMuted)),
          ]),
        ),
      );
}

class _TableSkeleton extends StatelessWidget {
  const _TableSkeleton();
  @override
  Widget build(BuildContext context) => Container(
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), border: Border.all(color: kBorder)),
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
        child: Column(children: List.generate(7, (_) => const Padding(
          padding: EdgeInsets.symmetric(vertical: 14),
          child: Row(children: [
            Expanded(flex: 6, child: Row(children: [
              SkeletonBox(width: 42, height: 42, radius: 13),
              SizedBox(width: 14),
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                SkeletonBox(height: 13),
                SizedBox(height: 7),
                SkeletonBox(width: 70, height: 10),
              ])),
              SizedBox(width: 40),
            ])),
            Expanded(flex: 3, child: Align(alignment: Alignment.centerLeft, child: SkeletonBox(width: 100, height: 13))),
            Expanded(flex: 2, child: Align(alignment: Alignment.centerLeft, child: SkeletonBox(width: 72, height: 24, radius: 12))),
            Expanded(flex: 2, child: Align(alignment: Alignment.centerLeft, child: SkeletonBox(width: 80, height: 13))),
            SizedBox(width: 28),
          ]),
        ))),
      );
}
