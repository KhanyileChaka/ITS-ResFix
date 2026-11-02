import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../models/complaint_model.dart';
import '../../utils/constants.dart';
import '../../utils/responsive.dart';
import 'package:provider/provider.dart';
import '../../providers/complaint_provider.dart';
import '../../widgets/conversation.dart';
import '../../widgets/status_badge.dart';
import '../../widgets/status_timeline.dart';
import '../../widgets/ui.dart';
import '../../widgets/visuals.dart';
import 'rate_repair_screen.dart';

class ComplaintDetailScreen extends StatelessWidget {
  final ComplaintModel complaint;
  /// Layout tests pass sample messages so no network is needed.
  final List<MessageModel>? debugMessages;
  const ComplaintDetailScreen({super.key, required this.complaint, this.debugMessages});

  @override
  Widget build(BuildContext context) {
    // Prefer the freshest copy (an appeal or a status change can land while this page is open).
    final c = context.watch<ComplaintProvider>().complaints.firstWhere((x) => x.complaintId == complaint.complaintId, orElse: () => complaint);
    final hasAi = c.aiNote != null && c.aiNote!.trim().isNotEmpty;
    final hasPhotoNote = c.photoNote != null && c.photoNote!.trim().isNotEmpty;
    final hasWorkerNote = c.workerNotes != null && c.workerNotes!.trim().isNotEmpty;
    String fmt(DateTime d) => DateFormat('d MMM yyyy, HH:mm').format(d.toLocal());

    return Scaffold(
      backgroundColor: kBg,
      appBar: AppBar(
        leading: modalLeading(context),
        title: Text(c.jobCode),
        actions: [StatusBadge(statusId: c.statusId, statusName: c.statusName), const SizedBox(width: 16)],
      ),
      body: ListView(padding: pagePadding(context), children: [
        // Summary
        Container(
          padding: const EdgeInsets.all(18),
          decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(20), border: Border.all(color: kBorder), boxShadow: kCardShadow),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Container(
                width: 52, height: 52,
                decoration: BoxDecoration(color: statusSoftColor(c.statusId), borderRadius: BorderRadius.circular(16)),
                child: Icon(categoryIcon(c.categoryName), color: statusColor(c.statusId), size: 26),
              ),
              const SizedBox(width: 14),
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(c.categoryName, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: kText)),
                const SizedBox(height: 3),
                Text('Sent ${fmt(c.createdAt)}', style: const TextStyle(fontSize: 13, color: kMuted)),
              ])),
              if (c.isEmergency)
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                  decoration: BoxDecoration(color: const Color(0xFFFDECEC), borderRadius: BorderRadius.circular(20)),
                  child: const Text('EMERGENCY', style: TextStyle(color: kRed, fontSize: 11, fontWeight: FontWeight.w800)),
                ),
            ]),
            const Padding(padding: EdgeInsets.symmetric(vertical: 16), child: Divider(height: 1)),
            const Text('What you reported', style: TextStyle(fontSize: 12, color: kMuted, fontWeight: FontWeight.w600)),
            const SizedBox(height: 6),
            SelectableText(c.description, style: const TextStyle(fontSize: 15.5, color: kText, height: 1.5)),
          ]),
        ),

        if (c.photoUrl != null) ...[
          const SizedBox(height: 20),
          const SectionLabel('Your photo'),
          const SizedBox(height: 10),
          PhotoThumb(url: c.photoUrl!),
        ],

        if (hasPhotoNote) ...[
          const SizedBox(height: 16),
          Callout(
            color: kAmberSoft, border: kAmber, icon: Icons.image_not_supported_outlined,
            title: 'Your photo was not attached',
            body: c.photoNote!,
          ),
        ],

        if (hasAi) ...[
          const SizedBox(height: 16),
          Callout(
            color: c.isRejected ? const Color(0xFFFDECEC) : kGreenSoft,
            border: c.isRejected ? kRed : kGreen,
            icon: c.isRejected ? Icons.cancel_outlined : Icons.fact_check_outlined,
            title: c.isRejected ? 'Why it was rejected' : "Assistant's review",
            body: c.aiNote!,
          ),
        ],

        if (c.isRejected) ...[
          const SizedBox(height: 16),
          AppealSection(complaint: c),
        ],

        const SizedBox(height: 20),
        const SectionLabel('Details'),
        const SizedBox(height: 10),
        InfoPanel(children: [
          InfoRow(Icons.meeting_room_outlined, 'Room', c.roomNumber),
          InfoRow(Icons.flag_outlined, 'Priority', c.priority, valueColor: c.isEmergency ? kRed : null),
          if (c.originalCategoryName != null) InfoRow(Icons.swap_horiz_rounded, 'You picked', '${c.originalCategoryName} (moved by the assistant)'),
          if (c.workerName != null) InfoRow(Icons.engineering_outlined, 'Assigned to', c.workerName!),
          if (c.deadline != null && !c.isResolved && !c.isRejected)
            InfoRow(Icons.timer_outlined, 'Time left', c.timerLabel, valueColor: c.isExpired ? kRed : null),
          if (c.resolvedAt != null) InfoRow(Icons.check_circle_outline, 'Resolved on', fmt(c.resolvedAt!), valueColor: kGreenDark),
          InfoRow(Icons.tag_rounded, 'Reference', c.jobCode, last: true),
        ]),

        if (!c.isRejected) ...[
          const SizedBox(height: 20),
          MessagesSection(complaintId: c.complaintId, canSend: c.statusId == 1 || c.statusId == 2, debugMessages: debugMessages),
        ],

        if (hasWorkerNote) ...[
          const SizedBox(height: 16),
          Callout(color: kBlueSoft, border: kBlue, icon: Icons.sticky_note_2_outlined, title: 'Note from the technician', body: c.workerNotes!),
        ],

        const SizedBox(height: 20),
        const SectionLabel('Progress'),
        const SizedBox(height: 10),
        Container(
          width: double.infinity,
          padding: const EdgeInsets.fromLTRB(18, 18, 18, 8),
          decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), border: Border.all(color: kBorder)),
          child: StatusTimeline(complaint: c),
        ),

        if (c.canRate) ...[
          const SizedBox(height: 22),
          ElevatedButton.icon(
            onPressed: () => replacePage(context, RateRepairScreen(complaint: c), width: 560),
            icon: const Icon(Icons.star_rounded, color: Colors.white),
            label: const Text('Rate this repair'),
          ),
        ] else if (c.isResolved && c.rating != null) ...[
          const SizedBox(height: 22),
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(color: kGreenSoft, borderRadius: BorderRadius.circular(16), border: Border.all(color: kGreen.withOpacity(.35))),
            child: Row(children: [
              const Icon(Icons.star_rounded, color: kAmber),
              const SizedBox(width: 10),
              Expanded(child: Text('You rated this repair ${c.rating}/5.', style: const TextStyle(color: kGreenDark, fontWeight: FontWeight.w700))),
            ]),
          ),
        ],
        const SizedBox(height: 12),
      ]),
    );
  }
}
