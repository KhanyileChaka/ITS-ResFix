class ComplaintModel {
  final int complaintId;
  final int studentId;
  final int categoryId;
  final String categoryName;
  final String roomNumber;
  final String description;
  final String? photoUrl;
  final int statusId;
  final String statusName;
  final String priority;
  final String? workerName;
  final DateTime? deadline;
  final DateTime? assignedAt;
  final DateTime? resolvedAt;
  final DateTime createdAt;
  final String timerStatus;
  final String? workerNotes;
  final int? rating;
  final String? ratingComment;
  final bool ratingResolved;
  final String? aiNote;
  final String? photoNote;
  final String? originalCategoryName;
  final String? appealStatus; // null, pending, approved, declined
  final String? appealReason;
  final String? appealResponse;
  final bool isRejected;

  const ComplaintModel({
    required this.complaintId,
    required this.studentId,
    required this.categoryId,
    required this.categoryName,
    required this.roomNumber,
    required this.description,
    this.photoUrl,
    required this.statusId,
    required this.statusName,
    required this.priority,
    this.workerName,
    this.deadline,
    this.assignedAt,
    this.resolvedAt,
    required this.createdAt,
    required this.timerStatus,
    this.workerNotes,
    this.rating,
    this.ratingComment,
    this.ratingResolved = false,
    this.aiNote,
    this.photoNote,
    this.originalCategoryName,
    this.appealStatus,
    this.appealReason,
    this.appealResponse,
    this.isRejected = false,
  });

  String get jobCode => 'C${complaintId.toString().padLeft(3, '0')}';
  bool get isEmergency => priority == 'Emergency';
  bool get isResolved => statusId == 3;
  bool get canRate => (statusId == 3) && rating == null;

  String get timerLabel {
    if (isResolved) return 'Completed';
    if (deadline == null) return 'Pending';
    final now = DateTime.now().toUtc();
    final diff = deadline!.toUtc().difference(now);
    if (diff.isNegative) return 'EXPIRED';
    final h = diff.inHours;
    if (h >= 24) return '${diff.inDays}d left';
    return '${h}h left';
  }

  bool get isExpired {
    if (deadline == null || isResolved) return false;
    return deadline!.toUtc().isBefore(DateTime.now().toUtc());
  }
}

class MessageModel {
  final int id;
  final bool fromStudent;
  final String? senderName;
  final String body;
  final String? photoUrl;
  final DateTime createdAt;
  /// The other side has opened the conversation and seen this message.
  final bool seen;
  const MessageModel({required this.id, required this.fromStudent, this.senderName, required this.body, this.photoUrl, required this.createdAt, this.seen = false});
}

class CategoryModel {
  final int categoryId;
  final String name;
  const CategoryModel({required this.categoryId, required this.name});
}

class NotificationModel {
  final int id;
  final String message;
  final DateTime createdAt;
  final bool isRead;
  const NotificationModel({
    required this.id,
    required this.message,
    required this.createdAt,
    required this.isRead,
  });
}
