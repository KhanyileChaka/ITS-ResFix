import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../models/student_model.dart';
import '../services/api_client.dart';
import '../services/auth_service.dart';

class AuthProvider extends ChangeNotifier {
  final _service = AuthService();

  StudentModel? _student;
  bool _loading = false;
  String? _error;

  AuthProvider() {
    // Server says the session is no longer valid: drop it and let the UI return to the login screen.
    ApiClient.instance.onUnauthorized = () => logout();
  }

  StudentModel? get student => _student;
  bool get loading => _loading;
  String? get error => _error;
  bool get isLoggedIn => _student != null;

  static const _sessionKey = 'resfix_session_v2';

  void _setLoading(bool v) { _loading = v; notifyListeners(); }
  void _setError(String? v) { _error = v; notifyListeners(); }

  /// Restores a saved session after a browser refresh. The token is signed by the server and expires after 7 days,
  /// so a stale or tampered value is simply rejected on the first request.
  Future<void> restore() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final raw = prefs.getString(_sessionKey);
      if (raw == null) return;
      final data = jsonDecode(raw) as Map<String, dynamic>;
      ApiClient.instance.token = data['token'] as String;
      _student = StudentModel.fromJson(data['student'] as Map<String, dynamic>);
    } catch (_) {
      _student = null;
      ApiClient.instance.token = null;
    }
  }

  Future<void> _startSession(LoginResult r) async {
    ApiClient.instance.token = r.token;
    _student = r.student;
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_sessionKey, jsonEncode({'token': r.token, 'student': r.student.toJson()}));
    } catch (_) {}
  }

  Future<void> _clearSession() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.remove(_sessionKey);
    } catch (_) {}
  }

  Future<bool> login(String cellphone, String password) async {
    _setLoading(true); _setError(null);
    try {
      await _startSession(await _service.loginWithCellphone(cellphone, password));
      notifyListeners();
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    } finally {
      _setLoading(false);
    }
  }

  Future<Map<String, dynamic>?> checkFirstLogin(String cellphone) async {
    _setLoading(true); _setError(null);
    try {
      return await _service.checkFirstLogin(cellphone);
    } catch (e) {
      _setError(e.toString());
      return null;
    } finally {
      _setLoading(false);
    }
  }

  Future<bool> generateOtp(String cellphone) async {
    _setLoading(true); _setError(null);
    try {
      await _service.generateOtp(cellphone);
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    } finally {
      _setLoading(false);
    }
  }

  Future<bool> verifyOtp(String cellphone, String otp) async {
    _setLoading(true); _setError(null);
    try {
      await _service.verifyOtp(cellphone, otp);
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    } finally {
      _setLoading(false);
    }
  }

  Future<bool> setPassword(String cellphone, String password) async {
    _setLoading(true); _setError(null);
    try {
      await _startSession(await _service.setPassword(cellphone, password));
      notifyListeners();
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    } finally {
      _setLoading(false);
    }
  }

  Future<bool> _guarded(Future<void> Function() job) async {
    _setLoading(true); _setError(null);
    try {
      await job();
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    } finally {
      _setLoading(false);
    }
  }

  Future<bool> requestPasswordReset(String cellphone) => _guarded(() => _service.requestPasswordReset(cellphone));
  Future<bool> verifyPasswordReset(String cellphone, String code) => _guarded(() => _service.verifyPasswordReset(cellphone, code));
  Future<bool> resetPassword(String password) => _guarded(() => _service.resetPassword(password));

  /// Turns SMS alerts on or off for this student. Returns false (with [error] set) when the server refuses.
  Future<bool> setSmsAlerts(bool enabled) async {
    if (_student == null) return false;
    try {
      await _service.setSmsAlerts(enabled);
      _student = _student!.copyWith(smsAlerts: enabled);
      try {
        final prefs = await SharedPreferences.getInstance();
        final raw = prefs.getString(_sessionKey);
        if (raw != null) {
          final data = jsonDecode(raw) as Map<String, dynamic>;
          data['student'] = _student!.toJson();
          await prefs.setString(_sessionKey, jsonEncode(data));
        }
      } catch (_) {}
      notifyListeners();
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    }
  }

  Future<bool> changePassword(String current, String newPwd) async {
    if (_student == null) return false;
    _setLoading(true); _setError(null);
    try {
      await _service.changePassword(current, newPwd);
      return true;
    } catch (e) {
      _setError(e.toString());
      return false;
    } finally {
      _setLoading(false);
    }
  }

  @visibleForTesting
  void debugSetStudent(StudentModel s) {
    _student = s;
    notifyListeners();
  }

  void logout() {
    if (_student == null && ApiClient.instance.token == null) return;
    ApiClient.instance.token = null;
    _clearSession();
    _student = null;
    _error = null;
    notifyListeners();
  }

  void clearError() { _error = null; notifyListeners(); }
}
