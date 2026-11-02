import 'package:flutter/material.dart';
import '../models/complaint_model.dart';
import '../utils/constants.dart';

class StatusTimeline extends StatelessWidget {
  final ComplaintModel complaint;
  const StatusTimeline({super.key, required this.complaint});

  Widget _rejectedTimeline() {
    Widget step({required Color color, required String title, String? sub, String? badge, bool line = false}) {
      return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(
          width: 28,
          child: Column(children: [
            Container(width: 14, height: 14, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
            if (line) Container(width: 2, height: 36, color: kBorder),
          ]),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Text(title, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14, color: kText)),
                if (badge != null) ...[
                  const SizedBox(width: 8),
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                    decoration: BoxDecoration(color: const Color(0xFFFDECEC), borderRadius: BorderRadius.circular(999)),
                    child: Text(badge, style: const TextStyle(color: kRed, fontSize: 10, fontWeight: FontWeight.w800)),
                  ),
                ],
              ]),
              if (sub != null) Text(sub, style: const TextStyle(fontSize: 12, color: kMuted)),
            ]),
          ),
        ),
      ]);
    }

    return Column(children: [
      step(color: kGreen, title: 'Sent', sub: _formatDate(complaint.createdAt), line: true),
      step(color: kRed, title: 'Rejected', sub: 'Not a maintenance issue, or details missing', badge: 'FINAL'),
    ]);
  }

  @override
  Widget build(BuildContext context) {
    if (complaint.isRejected) return _rejectedTimeline();
    final steps = [
      _Step('Submitted', 1, Icons.check_circle_outline),
      _Step('Under Review', 1, Icons.search),
      _Step('In Progress', 2, Icons.build_outlined),
      _Step('Resolved', 3, Icons.verified_outlined),
      _Step('Closed', 3, Icons.lock_outline),
    ];

    return Column(
      children: List.generate(steps.length, (i) {
        final step = steps[i];
        final isActive = complaint.statusId == step.statusId && i != steps.length - 1;
        final isDone = complaint.statusId > step.statusId ||
            (complaint.statusId == step.statusId && i < steps.length - 1 && complaint.statusId >= step.statusId);
        final isCurrent = complaint.statusId == step.statusId;
        final isLast = i == steps.length - 1;

        Color dotColor;
        if (isCurrent && !isLast) {
          dotColor = kGreen;
        } else if (isDone && i < _currentIndex(complaint)) {
          dotColor = kGreen;
        } else {
          dotColor = const Color(0xFFCBD5E1);
        }

        return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(
            width: 28,
            child: Column(children: [
              Container(
                width: 14, height: 14,
                decoration: BoxDecoration(
                  color: _dotColor(i, complaint),
                  shape: BoxShape.circle,
                  border: Border.all(color: _dotColor(i, complaint) == const Color(0xFFCBD5E1) ? kBorder : _dotColor(i, complaint), width: 2),
                ),
              ),
              if (!isLast)
                Container(width: 2, height: 36, color: i < _currentIndex(complaint) ? kGreen : kBorder),
            ]),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.only(top: 0, bottom: 10),
              child: Row(children: [
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Row(children: [
                      Text(
                        step.label,
                        style: TextStyle(
                          fontWeight: FontWeight.w600,
                          fontSize: 14,
                          color: _dotColor(i, complaint) == const Color(0xFFCBD5E1) ? kMuted : kText,
                        ),
                      ),
                      if (_isCurrentStep(i, complaint)) ...[
                        const SizedBox(width: 8),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                          decoration: BoxDecoration(
                            color: kGreenSoft,
                            borderRadius: BorderRadius.circular(999),
                          ),
                          child: const Text('ACTIVE', style: TextStyle(color: kGreenDark, fontSize: 10, fontWeight: FontWeight.w800)),
                        ),
                      ],
                    ]),
                    if (_isCurrentStep(i, complaint) && complaint.assignedAt != null)
                      Text(
                        _formatDate(complaint.assignedAt!),
                        style: const TextStyle(fontSize: 12, color: kMuted),
                      ),
                    if (i == 0 && complaint.createdAt != DateTime(0))
                      Text(_formatDate(complaint.createdAt), style: const TextStyle(fontSize: 12, color: kMuted)),
                  ]),
                ),
              ]),
            ),
          ),
        ]);
      }),
    );
  }

  bool _isCurrentStep(int i, ComplaintModel c) {
    final steps = [1, 1, 2, 3, 3];
    return c.statusId == steps[i] && i == _currentIndex(c);
  }

  int _currentIndex(ComplaintModel c) {
    switch (c.statusId) {
      case 1: return 0;
      case 2: return 2;
      case 3: return c.resolvedAt != null ? 4 : 3;
      default: return 0;
    }
  }

  Color _dotColor(int i, ComplaintModel c) {
    final current = _currentIndex(c);
    if (i < current) return kGreen;
    if (i == current) return kGreen;
    return const Color(0xFFCBD5E1);
  }

  String _formatDate(DateTime dt) {
    final local = dt.toLocal();
    return '${local.day} ${_month(local.month)} ${local.year} · ${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
  }

  String _month(int m) => ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][m-1];
}

class _Step {
  final String label;
  final int statusId;
  final IconData icon;
  const _Step(this.label, this.statusId, this.icon);
}
