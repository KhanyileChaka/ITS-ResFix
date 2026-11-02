import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../utils/constants.dart';
import '../../widgets/auth_shell.dart';
import 'otp_screen.dart';

class FirstLoginScreen extends StatefulWidget {
  const FirstLoginScreen({super.key});
  @override
  State<FirstLoginScreen> createState() => _FirstLoginScreenState();
}

class _FirstLoginScreenState extends State<FirstLoginScreen> {
  final _phoneCtrl = TextEditingController();

  @override
  void dispose() {
    _phoneCtrl.dispose();
    super.dispose();
  }

  Future<void> _enter() async {
    final auth = context.read<AuthProvider>();
    final phone = _phoneCtrl.text.trim();
    if (phone.isEmpty) return;

    final data = await auth.checkFirstLogin(phone);
    if (data == null || !mounted) return;

    if (data['registered'] == true) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('This number is already registered. Please log in.')));
      return;
    }

    final sent = await auth.generateOtp(phone);
    if (!mounted || !sent) return;

    Navigator.push(context, MaterialPageRoute(builder: (_) => OtpScreen(cellphone: phone, studentData: data)));
  }

  @override
  Widget build(BuildContext context) {
    return AuthShell(
      title: 'Set up your account',
      subtitle: 'Enter the cellphone number your accommodation manager registered for you. We will send you a code to confirm it is you.',
      child: Consumer<AuthProvider>(builder: (_, auth, __) {
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (auth.error != null) AuthError('${auth.error!}  If your number is not registered, ask your accommodation manager to add it.'),
          const FieldLabel('Cellphone number'),
          TextField(
            controller: _phoneCtrl,
            keyboardType: TextInputType.phone,
            onSubmitted: (_) => auth.loading ? null : _enter(),
            decoration: const InputDecoration(hintText: '071 000 0000', prefixIcon: Icon(Icons.phone_outlined, color: kMuted)),
          ),
          const SizedBox(height: 22),
          ElevatedButton(onPressed: auth.loading ? null : _enter, child: auth.loading ? const ButtonSpinner() : const Text('Send me a code')),
          const SizedBox(height: 14),
          Center(
            child: TextButton.icon(
              onPressed: () => Navigator.pop(context),
              icon: const Icon(Icons.arrow_back_rounded, size: 18, color: kMuted),
              label: const Text('Back to log in', style: TextStyle(color: kMuted, fontWeight: FontWeight.w700)),
            ),
          ),
        ]);
      }),
    );
  }
}
