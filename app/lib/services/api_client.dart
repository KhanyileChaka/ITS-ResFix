import 'dart:convert';
import 'dart:typed_data';
import 'package:http/http.dart' as http;
import '../utils/constants.dart';

class ApiException implements Exception {
  final int status;
  final String message;
  ApiException(this.status, this.message);
  @override
  String toString() => message;
}

/// Thin client for the ITS ResFix student API. Holds the bearer token and reports expired sessions.
class ApiClient {
  ApiClient._();
  static final ApiClient instance = ApiClient._();

  String? token;
  void Function()? onUnauthorized;

  static const _timeout = Duration(seconds: 25);

  Map<String, String> _headers({bool json = true}) => {
        if (json) 'Content-Type': 'application/json',
        if (token != null) 'Authorization': 'Bearer $token',
      };

  Uri _uri(String path) => Uri.parse('$kApiBaseUrl/api/student/$path');

  Future<dynamic> get(String path) => _send(() => http.get(_uri(path), headers: _headers()));

  Future<dynamic> post(String path, [Map<String, dynamic>? body, String? bearer]) => _send(() => http.post(
        _uri(path),
        headers: {..._headers(), if (bearer != null) 'Authorization': 'Bearer $bearer'},
        body: jsonEncode(body ?? {}),
      ));

  Future<dynamic> postMultipart(String path, Map<String, String> fields, {Uint8List? file, String? fileName, String? contentType}) async {
    return _send(() async {
      final req = http.MultipartRequest('POST', _uri(path));
      req.headers.addAll(_headers(json: false));
      req.fields.addAll(fields);
      if (file != null) {
        req.files.add(http.MultipartFile.fromBytes('photo', file, filename: fileName ?? 'photo.jpg'));
      }
      return http.Response.fromStream(await req.send());
    });
  }

  Future<dynamic> _send(Future<http.Response> Function() call) async {
    http.Response res;
    try {
      res = await call().timeout(_timeout);
    } on Exception {
      throw ApiException(0, 'Cannot reach the server. Check your connection and try again.');
    }
    dynamic body;
    try {
      body = res.body.isEmpty ? null : jsonDecode(res.body);
    } catch (_) {
      body = null;
    }
    if (res.statusCode >= 200 && res.statusCode < 300) return body;

    final msg = (body is Map && body['error'] is String)
        ? body['error'] as String
        : res.statusCode == 429
            ? 'Too many requests. Please wait a moment and try again.'
            : 'Something went wrong (${res.statusCode}).';
    // 401 on an authenticated call means the session expired; login failures also use 401 but carry no token.
    if (res.statusCode == 401 && token != null) onUnauthorized?.call();
    throw ApiException(res.statusCode, msg);
  }
}
