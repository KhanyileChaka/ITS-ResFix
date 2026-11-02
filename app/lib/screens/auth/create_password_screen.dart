import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/complaint_provider.dart';
import '../../utils/constants.dart';
import '../../widgets/auth_shell.dart';
import '../main/main_scaffold.dart';

class CreatePasswordScreen extends StatefulWidget {
  final String cellphone;
  final Map<String, dynamic> studentData;
  const CreatePasswordScreen({super.key, required this.cellphone, required this.studentData});
  @override
  State<CreatePasswordScreen> createState() => _CreatePasswordScreenState();
}

class _CreatePasswordScreenState extends State<CreatePasswordScreen> {
  final _newCtrl = TextEditingController();
  final _confirmCtrl = TextEditingController();
  bool _show = false;
  String? _localError;

  bool get _hasMin8 => _newCtrl.text.length >= 8;
  bool get _hasNumber => _newCtrl.text.contains(RegExp(r'\d'));
  bool get _hasUpper => _newCtrl.text.contains(RegExp(r'[A-Z]'));
  bool get _matches => _confirmCtrl.text.isNotEmpty && _newCtrl.text == _confirmCtrl.text;

  @override
  void dispose() {
    _newCtrl.dispose();
    _confirmCtrl.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!_hasMin8 || !_hasNumber || !_hasUpper) {
      setState(() => _localError = 'Your password does not meet all the requirements yet.');
      return;
    }
    if (_newCtrl.text != _confirmCtrl.text) {
      setState(() => _localError = 'The two passwords do not match.');
      return;
    }
    setState(() => _localError = null);
    final auth = context.read<AuthProvider>();
    final ok = await auth.setPassword(widget.cellphone, _newCtrl.text);
    if (ok && mounted) {
      await context.read<ComplaintProvider>().loadComplaints();
      if (!mounted) return;
      Navigator.pushAndRemoveUntil(context, MaterialPageRoute(builder: (_) => const MainScaffold()), (_) => false);
    }
  }

  Widget _rule(bool met, String label) => Padding(
        padding: const EdgeInsets.only(bottom: 6),
        child: Row(children: [
          Icon(met ? Icons.check_circle : Icons.circle_outlined, size: 17, color: met ? kGreenDark : kMuted),
          const SizedBox(width: 8),
          Text(label, style: TextStyle(fontSize: 13.5, color: met ? kText : kMuted, fontWeight: met ? FontWeight.w600 : FontWeight.w400)),
        ]),
      );

  @override
  Widget build(BuildContext context) {
    final eye = IconButton(
      icon: Icon(_show ? Icons.visibility_off_outlined : Icons.visibility_outlined, color: kMuted),
      onPressed: () => setState(() => _show = !_show),
    );
    return AuthShell(
      title: 'Create your password',
      subtitle: 'Choose a password you will use every time you log in. Keep it private.',
      child: Consumer<AuthProvider>(builder: (_, auth, __) {
        final error = _localError ?? auth.error;
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (error != null) AuthError(error),
          const FieldLabel('New password'),
          TextField(
            controller: _newCtrl,
            obscureText: !_show,
            onChanged: (_) => setState(() => _localError = null),
            decoration: InputDecoration(hintText: 'Create a password', prefixIcon: const Icon(Icons.lock_outline, color: kMuted), suffixIcon: eye),
          ),
          const SizedBox(height: 16),
          const FieldLabel('Confirm password'),
          TextField(
            controller: _confirmCtrl,
            obscureText: !_show,
            onChanged: (_) => setState(() => _localError = null),
            onSubmitted: (_) => auth.loading ? null : _save(),
            decoration: const InputDecoration(hintText: 'Type it again', prefixIcon: Icon(Icons.lock_outline, color: kMuted)),
          ),
          const SizedBox(height: 18),
          _rule(_hasMin8, 'At least 8 characters'),
          _rule(_hasNumber, 'Contains a number'),
          _rule(_hasUpper, 'Contains a capital letter'),
          _rule(_matches, 'Both passwords match'),
          const SizedBox(height: 18),
          ElevatedButton(onPressed: auth.loading ? null : _save, child: auth.loading ? const ButtonSpinner() : const Text('Save and continue')),
        ]);
      }),
    );
  }
}
