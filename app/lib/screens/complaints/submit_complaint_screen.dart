import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/complaint_provider.dart';
import '../../utils/constants.dart';
import '../../utils/responsive.dart';
import '../../widgets/skeleton.dart';
import '../../widgets/ui.dart';
import '../../widgets/visuals.dart';

class SubmitComplaintScreen extends StatefulWidget {
  final bool startAsEmergency;
  const SubmitComplaintScreen({super.key, this.startAsEmergency = false});
  @override
  State<SubmitComplaintScreen> createState() => _SubmitComplaintScreenState();
}

class _SubmitComplaintScreenState extends State<SubmitComplaintScreen> {
  final _formKey = GlobalKey<FormState>();
  final _descCtrl = TextEditingController();
  final _scroll = ScrollController();
  int? _selectedCategory;
  bool _isEmergency = false;
  Uint8List? _photo;
  String? _photoName;
  bool _submitting = false;
  bool _reviewing = false;
  bool _reviewTimedOut = false;
  Map<String, dynamic>? _verdict;

  bool get _accepted => _verdict != null && _verdict!['status_id'] != 4;
  bool get _rejected => _verdict != null && _verdict!['status_id'] == 4;

  @override
  void initState() {
    super.initState();
    _isEmergency = widget.startAsEmergency;
    WidgetsBinding.instance.addPostFrameCallback((_) => context.read<ComplaintProvider>().loadCategories());
  }

  @override
  void dispose() {
    _descCtrl.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _pickPhoto() async {
    final picker = ImagePicker();
    ImageSource? source = ImageSource.gallery;
    // Phones can use the camera; on tablets and desktops go straight to the file picker.
    if (context.screenSize == ScreenSize.phone) {
      source = await showModalBottomSheet<ImageSource>(
        context: context,
        shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(24))),
        builder: (ctx) => SafeArea(child: Column(mainAxisSize: MainAxisSize.min, children: [
          const SizedBox(height: 12),
          ListTile(
            leading: const Icon(Icons.camera_alt_outlined, color: kGreenDark),
            title: const Text('Take a photo'),
            onTap: () => Navigator.pop(ctx, ImageSource.camera),
          ),
          ListTile(
            leading: const Icon(Icons.photo_library_outlined, color: kGreenDark),
            title: const Text('Choose from gallery'),
            onTap: () => Navigator.pop(ctx, ImageSource.gallery),
          ),
          const SizedBox(height: 8),
        ])),
      );
    }
    if (source == null) return;
    final picked = await picker.pickImage(source: source, maxWidth: 1200, imageQuality: 85);
    if (picked == null) return;
    final bytes = await picked.readAsBytes();
    if (bytes.length > 5 * 1024 * 1024) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Photo is too large (max 5 MB).')));
      return;
    }
    setState(() { _photo = bytes; _photoName = picked.name; });
  }

  Future<void> _submit() async {
    if (_selectedCategory == null) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Please choose what kind of problem it is.')));
      return;
    }
    if (!_formKey.currentState!.validate()) return;
    setState(() { _submitting = true; _verdict = null; _reviewTimedOut = false; });
    final cp = context.read<ComplaintProvider>();

    final id = await cp.submitComplaint(
      categoryId: _selectedCategory!,
      description: _descCtrl.text.trim(),
      isEmergency: _isEmergency,
      photo: _photo,
      photoName: _photoName,
    );
    if (!mounted) return;

    if (id == null) {
      setState(() => _submitting = false);
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(cp.error ?? 'Failed to send complaint.')));
      return;
    }

    setState(() { _submitting = false; _reviewing = true; });
    if (_scroll.hasClients) _scroll.animateTo(0, duration: const Duration(milliseconds: 300), curve: Curves.easeOut);
    final verdict = await cp.waitForReview(id);
    if (!mounted) return;
    setState(() {
      _reviewing = false;
      _verdict = verdict;
      _reviewTimedOut = verdict == null;
    });
    cp.loadComplaints(silent: true);
  }

  // ── review feedback ──

  Widget _resultPanel() {
    if (_reviewing) {
      return _panel(
        color: kBlueSoft, border: kBlue,
        icon: const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: kBlue)),
        title: 'Reviewing your request…',
        body: 'Our assistant is checking the details and making sure it reaches the right team.',
      );
    }
    if (_reviewTimedOut) {
      return _panel(
        color: kAmberSoft, border: kAmber,
        icon: const Icon(Icons.schedule, color: kAmber),
        title: 'Received, review still in progress',
        body: 'You will get a notification with the outcome shortly. You can also check it under History.',
      );
    }
    if (_rejected) {
      return _panel(
        color: const Color(0xFFFDECEC), border: kRed,
        icon: const Icon(Icons.cancel_outlined, color: kRed),
        title: 'Request not accepted',
        body: (_verdict!['ai_note'] as String?) ?? 'This does not look like a maintenance issue.',
        footer: 'Edit the description below and send it again if this was a real fault.',
      );
    }
    if (_accepted) {
      final moved = _verdict!['original_category_name'] != null;
      final emergency = _verdict!['priority'] == 'Emergency';
      final lines = <String>[
        if (moved) 'Moved from ${_verdict!['original_category_name']} to ${_verdict!['category_name']} so the right team handles it.'
        else 'Category: ${_verdict!['category_name']}.',
        if (emergency) 'Marked as an emergency.',
        if (_verdict!['ai_note'] != null && !moved) _verdict!['ai_note'] as String,
      ];
      return _panel(
        color: kGreenSoft, border: kGreen,
        icon: const Icon(Icons.check_circle_outline, color: kGreenDark),
        title: 'Request accepted',
        body: lines.join('\n'),
        action: ElevatedButton(onPressed: () => Navigator.pop(context), child: const Text('Done')),
      );
    }
    return const SizedBox.shrink();
  }

  Widget _photoWarning() {
    final note = _verdict?['photo_note'] as String?;
    if (note == null || note.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: 18),
      child: Callout(color: kAmberSoft, border: kAmber, icon: Icons.image_not_supported_outlined, title: 'Your photo was not attached', body: note),
    );
  }

  Widget _panel({required Color color, required Color border, required Widget icon, required String title, required String body, String? footer, Widget? action}) {
    return Container(
      margin: const EdgeInsets.only(bottom: 18),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(16), border: Border.all(color: border.withOpacity(.5))),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [icon, const SizedBox(width: 10), Expanded(child: Text(title, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15, color: kText)))]),
        const SizedBox(height: 8),
        Text(body, style: const TextStyle(color: kText, fontSize: 14, height: 1.5)),
        if (footer != null) ...[const SizedBox(height: 6), Text(footer, style: const TextStyle(color: kMuted, fontSize: 13))],
        if (action != null) ...[const SizedBox(height: 14), action],
      ]),
    );
  }

  // ── form ──

  Widget _categories(ComplaintProvider cp) {
    return LayoutBuilder(builder: (context, box) {
      final cols = box.maxWidth >= 520 ? 3 : 2;
      const gap = 10.0;
      final w = (box.maxWidth - gap * (cols - 1)) / cols;
      if (cp.categories.isEmpty) {
        return Shimmer(child: Wrap(spacing: gap, runSpacing: gap, children: List.generate(6, (_) => SizedBox(width: w, child: const SkeletonBox(height: 56, radius: 14)))));
      }
      return Wrap(
        spacing: gap, runSpacing: gap,
        children: cp.categories.map((c) {
          final sel = _selectedCategory == c.categoryId;
          return SizedBox(
            width: w,
            child: Material(
              color: sel ? kGreenSoft : Colors.white,
              borderRadius: BorderRadius.circular(14),
              child: InkWell(
                borderRadius: BorderRadius.circular(14),
                onTap: _submitting || _reviewing ? null : () => setState(() => _selectedCategory = c.categoryId),
                child: Container(
                  height: 56,
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(14),
                    border: Border.all(color: sel ? kGreen : kBorder, width: sel ? 2 : 1),
                  ),
                  child: Row(children: [
                    Icon(categoryIcon(c.name), size: 22, color: sel ? kGreenDark : kMuted),
                    const SizedBox(width: 10),
                    Expanded(child: Text(c.name, maxLines: 2, overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 13.5, fontWeight: sel ? FontWeight.w800 : FontWeight.w600, color: sel ? kGreenDark : kText, height: 1.15))),
                  ]),
                ),
              ),
            ),
          );
        }).toList(),
      );
    });
  }

  Widget _photoArea() {
    if (_photo != null) {
      return Stack(children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(16),
          child: Image.memory(_photo!, width: double.infinity, height: 190, fit: BoxFit.cover),
        ),
        Positioned(
          top: 10, right: 10,
          child: Material(
            color: Colors.black54, shape: const CircleBorder(),
            child: InkWell(
              customBorder: const CircleBorder(),
              onTap: () => setState(() { _photo = null; _photoName = null; }),
              child: const Padding(padding: EdgeInsets.all(7), child: Icon(Icons.close, color: Colors.white, size: 18)),
            ),
          ),
        ),
      ]);
    }
    return InkWell(
      borderRadius: BorderRadius.circular(16),
      onTap: _pickPhoto,
      child: Container(
        height: 116,
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16), border: Border.all(color: kBorder, width: 1.5)),
        child: const Column(mainAxisAlignment: MainAxisAlignment.center, children: [
          Icon(Icons.add_photo_alternate_outlined, color: kGreenDark, size: 32),
          SizedBox(height: 8),
          Text('Add a photo', style: TextStyle(fontWeight: FontWeight.w700, color: kText)),
          SizedBox(height: 2),
          Text('JPG, PNG or WebP · up to 5 MB', style: TextStyle(fontSize: 12, color: kMuted)),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final cp = context.watch<ComplaintProvider>();
    final room = context.read<AuthProvider>().student?.roomNumber ?? '';
    final locked = _submitting || _reviewing || _accepted;

    return Scaffold(
      backgroundColor: kBg,
      appBar: AppBar(leading: modalLeading(context), title: Text(widget.startAsEmergency ? 'Report an emergency' : 'Report an issue')),
      body: Form(
        key: _formKey,
        child: ListView(controller: _scroll, padding: pagePadding(context), children: [
          _resultPanel(),
          _photoWarning(),

          const SectionLabel('What kind of problem is it?'),
          const SizedBox(height: 12),
          _categories(cp),
          const SizedBox(height: 22),

          const SectionLabel('Where?'),
          const SizedBox(height: 12),
          Container(
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14), border: Border.all(color: kBorder)),
            child: Row(children: [
              const Icon(Icons.meeting_room_outlined, color: kMuted),
              const SizedBox(width: 12),
              Expanded(child: Text('Room $room', style: const TextStyle(fontWeight: FontWeight.w700, color: kText))),
              const Icon(Icons.lock_outline_rounded, size: 16, color: kMuted),
              const SizedBox(width: 6),
              const Text('Your room', style: TextStyle(fontSize: 12, color: kMuted)),
            ]),
          ),
          const SizedBox(height: 22),

          const SectionLabel('Describe the problem'),
          const SizedBox(height: 12),
          TextFormField(
            controller: _descCtrl,
            enabled: !locked,
            maxLines: 5,
            maxLength: 1000,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              hintText: 'What is wrong and where? e.g. "Water drips from the pipe under the kitchen sink"',
              alignLabelWithHint: true,
            ),
            validator: (v) => (v == null || v.trim().length < 10) ? 'Please write at least 10 characters so we can understand the problem.' : null,
          ),
          const SizedBox(height: 16),

          const SectionLabel('Photo (optional)'),
          const SizedBox(height: 12),
          _photoArea(),
          const SizedBox(height: 22),

          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            decoration: BoxDecoration(
              color: _isEmergency ? const Color(0xFFFEF2F2) : Colors.white,
              borderRadius: BorderRadius.circular(16),
              border: Border.all(color: _isEmergency ? kRed.withOpacity(.45) : kBorder),
            ),
            child: SwitchListTile(
              contentPadding: EdgeInsets.zero,
              secondary: Icon(Icons.warning_amber_rounded, color: _isEmergency ? kRed : kMuted),
              title: const Text('This is an emergency', style: TextStyle(fontWeight: FontWeight.w800, color: kText)),
              subtitle: const Text('Only for danger or serious damage: fire, smoke, flooding, exposed wires.',
                  style: TextStyle(fontSize: 12.5, color: kMuted, height: 1.35)),
              value: _isEmergency,
              activeColor: kRed,
              onChanged: locked ? null : (v) => setState(() => _isEmergency = v),
            ),
          ),
          const SizedBox(height: 26),

          ElevatedButton(
            onPressed: (_submitting || _reviewing || _accepted) ? null : _submit,
            child: _submitting
                ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2.5))
                : Text(_rejected ? 'Send again' : 'Send request'),
          ),
          const SizedBox(height: 12),
        ]),
      ),
    );
  }
}
