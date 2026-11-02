import 'dart:async';
import 'package:flutter/material.dart';
import 'package:pin_code_fields/pin_code_fields.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../utils/constants.dart';
import '../../widgets/auth_shell.dart';
import 'create_password_screen.dart';

class OtpScreen extends StatefulWidget {
  final String cellphone;
  final Map<String, dynamic> studentData;
  const OtpScreen({super.key, required this.cellphone, required this.studentData});
  @override
  State<OtpScreen> createState() => _OtpScreenState();
}

class _OtpScreenState extends State<OtpScreen> {
  String _otp = '';
  int _seconds = 299;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _startTimer();
  }

  void _startTimer() {
    _timer = Timer.periodic(const Duration(seconds: 1), (t) {
      if (_seconds == 0) { t.cancel(); return; }
      setState(() => _seconds--);
    });
  }

  String get _timerLabel {
    final m = (_seconds ~/ 60).toString().padLeft(2, '0');
    final s = (_seconds % 60).toString().padLeft(2, '0');
    return '$m:$s';
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _verify() async {
    if (_otp.length < 6) return;
    final auth = context.read<AuthProvider>();
    final ok = await auth.verifyOtp(widget.cellphone, _otp);
    if (ok && mounted) {
      Navigator.pushReplacement(context, MaterialPageRoute(
        builder: (_) => CreatePasswordScreen(cellphone: widget.cellphone, studentData: widget.studentData),
      ));
    }
  }

  Future<void> _resend() async {
    setState(() => _seconds = 299);
    _timer?.cancel();
    _startTimer();
    await context.read<AuthProvider>().generateOtp(widget.cellphone);
  }

  @override
  Widget build(BuildContext context) {
    return AuthShell(
      title: 'Enter your code',
      subtitle: 'We sent a 6-digit code to ${widget.cellphone}. It is valid for 5 minutes.',
      child: Consumer<AuthProvider>(builder: (_, auth, __) {
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (auth.error != null) AuthError('${auth.error!}  No code? Check that the cellphone number is the one your accommodation manager registered, or ask them to confirm it.'),
          LayoutBuilder(builder: (context, box) {
            final fieldW = ((box.maxWidth - 5 * 8) / 6).clamp(36.0, 52.0);
            return PinCodeTextField(
              appContext: context,
              length: 6,
              autoFocus: true,
              keyboardType: TextInputType.number,
              animationType: AnimationType.none,
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              pinTheme: PinTheme(
                shape: PinCodeFieldShape.box,
                borderRadius: BorderRadius.circular(12),
                fieldHeight: 58, fieldWidth: fieldW,
                activeFillColor: Colors.white,
                inactiveFillColor: Colors.white,
                selectedFillColor: kGreenSoft,
                activeColor: kGreen,
                inactiveColor: kBorder,
                selectedColor: kGreen,
              ),
              enableActiveFill: true,
              onChanged: (v) => _otp = v,
              onCompleted: (_) => _verify(),
            );
          }),
          const SizedBox(height: 10),
          Row(children: [
            const Icon(Icons.timer_outlined, size: 16, color: kMuted),
            const SizedBox(width: 6),
            Text(_seconds == 0 ? 'The code has expired' : 'Code expires in $_timerLabel',
                style: TextStyle(color: _seconds == 0 ? kRed : kMuted, fontSize: 13.5, fontWeight: FontWeight.w600)),
          ]),
          const SizedBox(height: 24),
          ElevatedButton(onPressed: auth.loading ? null : _verify, child: auth.loading ? const ButtonSpinner() : const Text('Verify code')),
          const SizedBox(height: 10),
          Center(
            child: TextButton(
              onPressed: _seconds == 0 ? _resend : null,
              child: Text('Send a new code', style: TextStyle(color: _seconds == 0 ? kGreenDark : kMuted, fontWeight: FontWeight.w700)),
            ),
          ),
        ]);
      }),
    );
  }
}
