import 'dart:typed_data';
import 'package:flutter/material.dart';
import '../models/complaint_model.dart';
import '../services/complaint_service.dart';

class ComplaintProvider extends ChangeNotifier {
  final _service = ComplaintService();

  List<ComplaintModel> _complaints = [];
  List<CategoryModel> _categories = [];
  List<Map<String, dynamic>> _notifications = [];
  bool _loading = false;
  bool _complaintsLoaded = false;      // true once the first fetch has finished (success or error)
  bool _notificationsLoaded = false;
  String? _error;

  List<ComplaintModel> get complaints => _complaints;
  List<CategoryModel> get categories => _categories;
  List<Map<String, dynamic>> get notifications => _notifications;
  bool get loading => _loading;
  /// False until the first complaints fetch finishes; screens show skeletons until then.
  bool get complaintsLoaded => _complaintsLoaded;
  bool get notificationsLoaded => _notificationsLoaded;
  String? get error => _error;

  int get activeCount => _complaints.where((c) => c.statusId == 2).length;
  int get resolvedCount => _complaints.where((c) => c.statusId == 3).length;
  int get pendingCount => _complaints.where((c) => c.statusId == 1).length;
  int get totalCount => _complaints.length;
  int get rejectedCount => _complaints.where((c) => c.statusId == 4).length;
  /// The newest request that is still open (submitted or in progress).
  ComplaintModel? get currentRequest {
    for (final c in _complaints) {
      if (c.statusId == 1 || c.statusId == 2) return c;
    }
    return null;
  }
  ComplaintModel? get toRate {
    for (final c in _complaints) {
      if (c.canRate) return c;
    }
    return null;
  }
  List<ComplaintModel> get recent => _complaints.take(5).toList();
  int get unreadNotifications => _notifications.where((n) => n['is_read'] == false).length;

  void _setLoading(bool v) { _loading = v; notifyListeners(); }
  void _setError(String? v) { _error = v; notifyListeners(); }

  Future<void> loadComplaints({bool silent = false}) async {
    if (!silent) { _setLoading(true); _setError(null); }
    try {
      _complaints = await _service.getComplaints();
      notifyListeners();
    } catch (e) {
      if (!silent) _setError(e.toString().replaceFirst('Exception: ', ''));
    } finally {
      _complaintsLoaded = true;
      if (!silent) { _setLoading(false); } else { notifyListeners(); }
    }
  }

  Future<Map<String, dynamic>?> waitForReview(int complaintId) async {
    try {
      return await _service.waitForReview(complaintId);
    } catch (_) {
      return null;
    }
  }

  Future<void> loadCategories() async {
    if (_categories.isNotEmpty) return;
    try {
      _categories = await _service.getCategories();
      notifyListeners();
    } catch (_) {}
  }

  /// Returns the new complaint id, or null on failure (see [error]).
  Future<int?> submitComplaint({
    required int categoryId,
    required String description,
    required bool isEmergency,
    Uint8List? photo,
    String? photoName,
  }) async {
    _setError(null);
    try {
      return await _service.submitComplaint(
        categoryId: categoryId,
        description: description,
        isEmergency: isEmergency,
        photo: photo,
        photoName: photoName,
      );
    } catch (e) {
      _setError(e.toString());
      return null;
    }
  }

  Future<bool> submitRating({
    required int complaintId,
    required int rating,
    required bool issueResolved,
    String? comment,
  }) async {
    _setLoading(true); _setError(null);
    try {
      await _service.submitRating(
        complaintId: complaintId,
        rating: rating,
        issueResolved: issueResolved,
        comment: comment,
      );
      await loadComplaints();
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    } finally {
      _setLoading(false);
    }
  }

  Future<bool> appeal(int complaintId, String reason) async {
    _setError(null);
    try {
      await _service.appeal(complaintId, reason);
      await loadComplaints(silent: true);
      await loadNotifications();
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    }
  }

  Future<List<MessageModel>> loadMessages(int complaintId) => _service.getMessages(complaintId);

  Future<({List<MessageModel> messages, String? typingName})> loadChat(int complaintId) => _service.getChat(complaintId);

  Future<void> sendTyping(int complaintId) => _service.sendTyping(complaintId);

  Future<MessageModel> sendMessage(int complaintId, {String? body, Uint8List? photo, String? photoName}) =>
      _service.sendMessage(complaintId, body: body, photo: photo, photoName: photoName);

  Future<void> loadNotifications() async {
    try {
      _notifications = await _service.getNotifications();
    } catch (_) {
      // keep whatever we had; the screen still leaves its loading state
    } finally {
      _notificationsLoaded = true;
      notifyListeners();
    }
  }

  Future<void> markRead(int id) async {
    final i = _notifications.indexWhere((n) => n['id'] == id);
    if (i < 0 || _notifications[i]['is_read'] == true) return;
    _notifications[i] = {..._notifications[i], 'is_read': true};
    notifyListeners();
    try {
      await _service.markNotificationRead(id);
    } catch (_) {}
  }

  Future<void> markAllRead() async {
    try {
      await _service.markAllNotificationsRead();
      _notifications = _notifications.map((n) => {...n, 'is_read': true}).toList();
      notifyListeners();
    } catch (_) {}
  }

  @visibleForTesting
  void debugSeed({List<ComplaintModel>? complaints, List<Map<String, dynamic>>? notifications, List<CategoryModel>? categories}) {
    if (complaints != null) { _complaints = complaints; _complaintsLoaded = true; }
    if (notifications != null) { _notifications = notifications; _notificationsLoaded = true; }
    if (categories != null) _categories = categories;
    notifyListeners();
  }

  void clear() {
    _complaints = [];
    _notifications = [];
    _complaintsLoaded = false;
    _notificationsLoaded = false;
    _error = null;
    notifyListeners();
  }
}
