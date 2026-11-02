import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../utils/constants.dart';
import '../../widgets/auth_shell.dart';
import '../main/main_scaffold.dart';
import 'first_login_screen.dart';
import 'forgot_password_screen.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _phoneCtrl = TextEditingController();
  final _passCtrl = TextEditingController();
  bool _obscure = true;

  @override
  void dispose() {
    _phoneCtrl.dispose();
    _passCtrl.dispose();
    super.dispose();
  }

  Future<void> _login() async {
    final auth = context.read<AuthProvider>();
    final ok = await auth.login(_phoneCtrl.text.trim(), _passCtrl.text);
    if (ok && mounted) {
      Navigator.pushReplacement(context, MaterialPageRoute(builder: (_) => const MainScaffold()));
    }
  }

  Future<void> _forgot() async {
    context.read<AuthProvider>().clearError();
    final done = await Navigator.push<bool>(context, MaterialPageRoute(builder: (_) => ForgotPasswordScreen(cellphone: _phoneCtrl.text.trim())));
    if (!mounted) return;
    context.read<AuthProvider>().clearError();
    if (done == true) {
      _passCtrl.clear();
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Password changed. Log in with your new password.')));
    }
  }

  @override
  Widget build(BuildContext context) {
    return AuthShell(
      title: 'Welcome back',
      subtitle: 'Log in to report problems in your room and follow their progress.',
      child: Consumer<AuthProvider>(builder: (_, auth, __) {
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (auth.error != null) AuthError(auth.error!),
          const FieldLabel('Cellphone number'),
          TextField(
            controller: _phoneCtrl,
            keyboardType: TextInputType.phone,
            textInputAction: TextInputAction.next,
            decoration: const InputDecoration(hintText: '071 234 5678', prefixIcon: Icon(Icons.phone_outlined, color: kMuted)),
          ),
          const SizedBox(height: 16),
          const FieldLabel('Password'),
          TextField(
            controller: _passCtrl,
            obscureText: _obscure,
            onSubmitted: (_) => auth.loading ? null : _login(),
            decoration: InputDecoration(
              hintText: 'Your password',
              prefixIcon: const Icon(Icons.lock_outline, color: kMuted),
              suffixIcon: IconButton(
                icon: Icon(_obscure ? Icons.visibility_off_outlined : Icons.visibility_outlined, color: kMuted),
                onPressed: () => setState(() => _obscure = !_obscure),
              ),
            ),
          ),
          Align(
            alignment: Alignment.centerRight,
            child: TextButton(
              onPressed: _forgot,
              child: const Text('Forgot password?', style: TextStyle(color: kGreenDark, fontWeight: FontWeight.w700)),
            ),
          ),
          const SizedBox(height: 4),
          ElevatedButton(onPressed: auth.loading ? null : _login, child: auth.loading ? const ButtonSpinner() : const Text('Log in')),
          const SizedBox(height: 22),
          const Row(children: [
            Expanded(child: Divider()),
            Padding(padding: EdgeInsets.symmetric(horizontal: 12), child: Text('New here?', style: TextStyle(color: kMuted, fontSize: 13))),
            Expanded(child: Divider()),
          ]),
          const SizedBox(height: 18),
          OutlinedButton(
            onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const FirstLoginScreen())),
            child: const Text('Set up your account (first login)'),
          ),
        ]);
      }),
    );
  }
}
