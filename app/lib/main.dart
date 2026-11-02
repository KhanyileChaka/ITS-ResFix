import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'providers/auth_provider.dart';
import 'providers/complaint_provider.dart';
import 'screens/auth/login_screen.dart';
import 'screens/main/main_scaffold.dart';
import 'utils/theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final auth = AuthProvider();
  await auth.restore();

  runApp(ITSResfixApp(auth: auth));
}

class ITSResfixApp extends StatelessWidget {
  final AuthProvider auth;
  const ITSResfixApp({super.key, required this.auth});

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider.value(value: auth),
        ChangeNotifierProvider(create: (_) => ComplaintProvider()),
      ],
      child: MaterialApp(
        title: 'ITS ResFix',
        debugShowCheckedModeBanner: false,
        theme: appTheme,
        home: auth.isLoggedIn ? const MainScaffold() : const LoginScreen(),
      ),
    );
  }
}
