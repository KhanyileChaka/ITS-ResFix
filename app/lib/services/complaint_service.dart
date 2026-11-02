import 'dart:typed_data';
import '../models/complaint_model.dart';
import 'api_client.dart';

class ComplaintService {
  final _api = ApiClient.instance;

  static DateTime? _dt(dynamic v) => v == null ? null : DateTime.parse(v as String);

  ComplaintModel _map(Map<String, dynamic> j) => ComplaintModel(
        complaintId: j['complaintId'] as int,
        studentId: 0,
        categoryId: j['categoryId'] as int,
        categoryName: j['categoryName'] as String,
        roomNumber: j['roomNumber'] as String,
        description: j['description'] as String,
        photoUrl: j['photoUrl'] as String?,
        statusId: j['statusId'] as int,
        statusName: j['statusName'] as String,
        priority: j['priority'] as String? ?? 'Normal',
        workerName: j['workerName'] as String?,
        deadline: _dt(j['deadline']),
        assignedAt: _dt(j['assignedAt']),
        resolvedAt: _dt(j['resolvedAt']),
        createdAt: DateTime.parse(j['createdAt'] as String),
        timerStatus: j['timerStatus'] as String? ?? 'pending',
        workerNotes: j['workerNotes'] as String?,
        rating: j['rating'] as int?,
        ratingComment: j['ratingComment'] as String?,
        aiNote: j['aiNote'] as String?,
        photoNote: j['photoNote'] as String?,
        originalCategoryName: j['originalCategoryName'] as String?,
        appealStatus: j['appealStatus'] as String?,
        appealReason: j['appealReason'] as String?,
        appealResponse: j['appealResponse'] as String?,
        isRejected: j['statusId'] == 4,
      );

  Future<List<ComplaintModel>> getComplaints() async {
    final r = await _api.get('complaints') as List;
    return r.map((e) => _map(Map<String, dynamic>.from(e as Map))).toList();
  }

  Future<List<CategoryModel>> getCategories() async {
    final r = await _api.get('categories') as List;
    return r.map((e) => CategoryModel(categoryId: e['categoryId'] as int, name: e['name'] as String)).toList();
  }

  /// The server takes the room number from the student's record and runs the AI review afterwards.
  Future<int> submitComplaint({
    required int categoryId,
    required String description,
    required bool isEmergency,
    Uint8List? photo,
    String? photoName,
  }) async {
    final r = await _api.postMultipart(
      'complaints',
      {'categoryId': '$categoryId', 'description': description, 'isEmergency': '$isEmergency'},
      file: photo,
      fileName: photoName,
    );
    return r['complaintId'] as int;
  }

  /// Polls until the AI reviewer has judged the complaint. Returns null on timeout.
  Future<Map<String, dynamic>?> waitForReview(int complaintId, {Duration timeout = const Duration(seconds: 40)}) async {
    final end = DateTime.now().add(timeout);
    while (DateTime.now().isBefore(end)) {
      final r = Map<String, dynamic>.from(await _api.get('complaints/$complaintId/review') as Map);
      if (r['ready'] == true) {
        return {
          'status_id': r['statusId'],
          'category_name': r['categoryName'],
          'original_category_name': r['originalCategoryName'],
          'priority': r['priority'],
          'ai_note': r['aiNote'],
          'photo_note': r['photoNote'],
        };
      }
      await Future.delayed(const Duration(seconds: 2));
    }
    return null;
  }

  Future<void> submitRating({
    required int complaintId,
    required int rating,
    required bool issueResolved,
    String? comment,
  }) async {
    await _api.post('complaints/$complaintId/rating', {
      'rating': rating,
      'issueResolved': issueResolved,
      'comment': comment,
    });
  }

  Future<void> appeal(int complaintId, String reason) async {
    await _api.post('complaints/$complaintId/appeal', {'reason': reason});
  }

  MessageModel _message(Map<String, dynamic> j) => MessageModel(
        id: (j['id'] as num).toInt(),
        fromStudent: j['sender'] == 'student',
        senderName: j['senderName'] as String?,
        body: j['body'] as String,
        photoUrl: j['photoUrl'] as String?,
        createdAt: DateTime.parse(j['createdAt'] as String),
        seen: j['seen'] == true,
      );

  /// The open conversation: the messages, and the name of whoever on the staff side is typing right now (or null).
  /// Asking for it marks the staff messages as seen.
  Future<({List<MessageModel> messages, String? typingName})> getChat(int complaintId) async {
    final r = Map<String, dynamic>.from(await _api.get('complaints/$complaintId/chat') as Map);
    final list = (r['messages'] as List).map((e) => _message(Map<String, dynamic>.from(e as Map))).toList();
    return (messages: list, typingName: r['typingName'] as String?);
  }

  Future<void> sendTyping(int complaintId) async {
    await _api.post('complaints/$complaintId/typing', {});
  }

  Future<List<MessageModel>> getMessages(int complaintId) async {
    final r = await _api.get('complaints/$complaintId/messages') as List;
    return r.map((e) => _message(Map<String, dynamic>.from(e as Map))).toList();
  }

  Future<MessageModel> sendMessage(int complaintId, {String? body, Uint8List? photo, String? photoName}) async {
    final r = await _api.postMultipart(
      'complaints/$complaintId/messages',
      {if (body != null && body.isNotEmpty) 'body': body},
      file: photo,
      fileName: photoName,
    );
    return _message(Map<String, dynamic>.from(r as Map));
  }

  Future<List<Map<String, dynamic>>> getNotifications() async {
    final r = await _api.get('notifications') as List;
    return r
        .map((e) => <String, dynamic>{
              'id': e['id'],
              'message': e['message'],
              'is_read': e['isRead'],
              'created_at': e['createdAt'],
              'complaint_id': e['complaintId'],
            })
        .toList();
  }

  Future<void> markNotificationRead(int id) async {
    await _api.post('notifications/$id/read');
  }

  Future<void> markAllNotificationsRead() async {
    await _api.post('notifications/read');
  }
}
