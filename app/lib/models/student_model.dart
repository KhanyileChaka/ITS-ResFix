class StudentModel {
  final int userId;
  final int studentId;
  final String email;
  final String fullName;
  final String studentNumber;
  final String roomNumber;
  final String cellphone;
  final bool isRegistered;
  final bool smsAlerts;
  final bool smsAvailable;
  final bool pushAvailable;

  const StudentModel({
    required this.userId,
    required this.studentId,
    required this.email,
    required this.fullName,
    required this.studentNumber,
    required this.roomNumber,
    required this.cellphone,
    required this.isRegistered,
    this.smsAlerts = false,
    this.smsAvailable = false,
    this.pushAvailable = false,
  });

  StudentModel copyWith({bool? smsAlerts}) => StudentModel(
        userId: userId, studentId: studentId, email: email, fullName: fullName, studentNumber: studentNumber,
        roomNumber: roomNumber, cellphone: cellphone, isRegistered: isRegistered,
        smsAlerts: smsAlerts ?? this.smsAlerts, smsAvailable: smsAvailable, pushAvailable: pushAvailable,
      );

  Map<String, dynamic> toJson() => {
        'userId': userId,
        'studentId': studentId,
        'email': email,
        'fullName': fullName,
        'studentNumber': studentNumber,
        'roomNumber': roomNumber,
        'cellphone': cellphone,
        'isRegistered': isRegistered,
        'smsAlerts': smsAlerts,
        'smsAvailable': smsAvailable,
        'pushAvailable': pushAvailable,
      };

  factory StudentModel.fromJson(Map<String, dynamic> j) => StudentModel(
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

  String get firstName => fullName.split(' ').first;
  String get initials {
    final parts = fullName.trim().split(' ');
    if (parts.length >= 2) return '${parts[0][0]}${parts[1][0]}'.toUpperCase();
    return fullName.isNotEmpty ? fullName[0].toUpperCase() : '?';
  }
}
