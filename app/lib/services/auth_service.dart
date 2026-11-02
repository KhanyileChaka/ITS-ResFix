import '../models/student_model.dart';
import 'api_client.dart';

class LoginResult {
  final String token;
  final StudentModel student;
  LoginResult(this.token, this.student);
}

/// All password checks, OTP checks and rate limits happen on the server; this class only calls the API.
class AuthService {
  final _api = ApiClient.instance;
  String? _setupToken;
  String? _resetToken;

  static StudentModel _student(Map<String, dynamic> j) => StudentModel(
        userId: j['userId'] as int,
        studentId: j['studentId'] as int,
        email: j['email'] as String,
        fullName: j['fullName'] as String,
        studentNumber: j['studentNumber'] as String,
        roomNumber: j['roomNumber'] as String,
        cellphone: j['cellphone'] as String,
        isRegistered: j['isRegistered'] as bool,
        smsAlerts: j['smsAlerts'] as bool? ?? false,
        smsAvailable: j['smsAvailable'] as bool? ?? false,
        pushAvailable: j['pushAvailable'] as bool? ?? false,
      );

  static LoginResult _result(dynamic r) =>
      LoginResult(r['token'] as String, _student(r['student'] as Map<String, dynamic>));

  Future<LoginResult> loginWithCellphone(String cellphone, String password) async =>
      _result(await _api.post('auth/login', {'cellphone': cellphone.trim(), 'password': password}));

  Future<Map<String, dynamic>> checkFirstLogin(String cellphone) async {
    final r = await _api.post('auth/check', {'cellphone': cellphone.trim()});
    return Map<String, dynamic>.from(r as Map);
  }

  Future<void> generateOtp(String cellphone) async {
    await _api.post('auth/otp/request', {'cellphone': cellphone.trim()});
  }

  Future<void> verifyOtp(String cellphone, String otp) async {
    final r = await _api.post('auth/otp/verify', {'cellphone': cellphone.trim(), 'code': otp.trim()});
    _setupToken = r['setupToken'] as String;
  }

  Future<LoginResult> setPassword(String cellphone, String password) async {
    final t = _setupToken;
    if (t == null) throw ApiException(401, 'Please verify your code again.');
    final r = _result(await _api.post('auth/password/set', {'newPassword': password}, t));
    _setupToken = null;
    return r;
  }

  Future<void> requestPasswordReset(String cellphone) async {
    await _api.post('auth/forgot/request', {'cellphone': cellphone.trim()});
  }

  Future<void> verifyPasswordReset(String cellphone, String code) async {
    final r = await _api.post('auth/forgot/verify', {'cellphone': cellphone.trim(), 'code': code.trim()});
    _resetToken = r['resetToken'] as String;
  }

  Future<void> resetPassword(String password) async {
    final t = _resetToken;
    if (t == null) throw ApiException(401, 'Please verify your code again.');
    await _api.post('auth/password/reset', {'newPassword': password}, t);
    _resetToken = null;
  }

  Future<void> changePassword(String currentPassword, String newPassword) async {
    await _api.post('password/change', {'current': currentPassword, 'newPassword': newPassword});
  }

  Future<void> setSmsAlerts(bool enabled) async {
    await _api.post('sms-alerts', {'enabled': enabled});
  }

  Future<StudentModel> getMe() async => _student(Map<String, dynamic>.from(await _api.get('me') as Map));
}
