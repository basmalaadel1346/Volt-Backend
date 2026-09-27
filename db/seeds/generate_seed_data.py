#!/usr/bin/env python3
"""Run: python3 db/seeds/generate_seed_data.py

Generates db/seeds/seed_dev_data.sql, db/seeds/remove_seed_data.sql and the
placeholder images the seed references. Every database rule the seed touches is
re-checked here in Python before anything is written."""
import os
import struct
import zlib
from collections import defaultdict
from decimal import Decimal, ROUND_HALF_UP

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SEED_DIR = f'{ROOT}/db/seeds'
IMG_DIR = f'{ROOT}/ElectroWorld/wwwroot/uploads/lessons'

# BCrypt hashes produced by the app's own Shared.Common.Implementations.PasswordHasher.
ADMIN_PASSWORD, ADMIN_HASH = 'Admin@Volt2026', '$2a$11$BiMMjyJU2jyoP4Sa02jUyulTGsYJzgfurlZpMKb.8GUxqMio/rlb6'
DEMO_PASSWORD, DEMO_HASH = 'Demo@Volt2026', '$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q'

PASS_PERCENTAGE = 75
PLACEMENT_QUESTIONS_PER_LEVEL = 4


def guid(n):
    return f'5EED0000-0000-4000-8000-{n:012d}'


# ---------------------------------------------------------------------------
# Users
# ---------------------------------------------------------------------------
USERS = [
    # key, id, email, hash, full name, role, provider, age, active, created days ago
    ('admin', guid(1), 'admin@volt.dev', ADMIN_HASH, 'مدير النظام', 'Admin', 'Email', None, True, 60),
    ('mona', guid(2), 'mona.parent@volt.dev', DEMO_HASH, 'منى عبد الرحمن', 'Parent', 'Email', None, True, 45),
    ('khaled', guid(3), 'khaled.parent@volt.dev', DEMO_HASH, 'خالد محمود', 'Parent', 'Email', None, True, 40),
    ('omar', guid(11), 'omar@volt.dev', DEMO_HASH, 'عمر ياسر', 'Child', 'Email', 9, True, 44),
    ('salma', guid(12), 'salma@volt.dev', DEMO_HASH, 'سلمى ياسر', 'Child', 'Email', 11, True, 44),
    ('youssef', guid(13), 'youssef@volt.dev', DEMO_HASH, 'يوسف خالد', 'Child', 'Email', 8, True, 39),
    ('laila', guid(14), 'laila@volt.dev', DEMO_HASH, 'ليلى حسن', 'Child', 'Email', 12, True, 30),
    ('disabled', guid(15), 'disabled.child@volt.dev', DEMO_HASH, 'حساب موقوف', 'Child', 'Email', 10, False, 25),
    ('guest', guid(21), None, None, 'ضيف', 'Child', 'Guest', None, True, 2),
]
USER_ID = {u[0]: u[1] for u in USERS}
PARENT_LINKS = [(101, 'mona', 'omar'), (102, 'mona', 'salma'), (103, 'khaled', 'youssef')]

# ---------------------------------------------------------------------------
# Content: levels, lessons, lesson contents
# ---------------------------------------------------------------------------
LEVELS = [
    (101, 'المستوى الأول: أساسيات الكهرباء', 'اكتشف ما هي الكهرباء، وكيف تعمل الدائرة الكهربية، وكيف تتعامل مع الكهرباء بأمان.'),
    (102, 'المستوى الثاني: المكونات الإلكترونية', 'تعرّف على الثنائي الباعث للضوء (LED) والمقاومة وزر الضغط، وابنِ دائرة آمنة.'),
]

LESSONS = [
    # id, level, sort, title, description, published, image, [text blocks]
    (1001, 101, 1, 'ما هي الكهرباء؟', 'نكتشف معًا الطاقة التي تشغّل ألعابنا وأجهزتنا كل يوم.', True, 'lesson-electricity',
     ['الكهرباء طاقة تنتج عن حركة شحنات صغيرة جدًا اسمها الإلكترونات داخل الأسلاك. البطارية مصدر صغير وآمن للكهرباء في ألعابنا، أما الكهرباء القوية التي تصل إلى بيوتنا فتُنتَج في محطات توليد الكهرباء.',
      'لا نستطيع رؤية الكهرباء بأعيننا، لكننا نرى آثارها: فهي تتحول إلى ضوء في المصباح، وحرارة في المكواة، وصوت في التلفاز. والبرق في السماء نوع من الكهرباء الطبيعية القوية.']),
    (1002, 101, 2, 'الدائرة الكهربية', 'بطارية وأسلاك ومصباح ومفتاح: كيف تسير الكهرباء؟', True, 'lesson-circuit',
     ['الدائرة الكهربية طريق مغلق تسير فيه الإلكترونات. تتكون أبسط دائرة من بطارية تمدّها بالطاقة، وأسلاك نحاسية تنقل الكهرباء، ومصباح يضيء.',
      'عندما تكون الدائرة مغلقة تمر الكهرباء فيضيء المصباح، وإذا انقطع السلك أو فتحنا المفتاح تصبح الدائرة مفتوحة فتتوقف الكهرباء وينطفئ المصباح.']),
    (1003, 101, 3, 'السلامة الكهربية', 'قواعد مهمة تحمينا ونحن نستخدم الكهرباء.', True, 'lesson-safety',
     ['الماء يوصل الكهرباء بسرعة، لذلك لا نلمس المقبس أو الأجهزة الكهربية بأيدٍ مبللة أبدًا، ولا ندخل أي جسم معدني في المقبس.',
      'تُغطّى الأسلاك بالبلاستيك لأنه مادة عازلة تحمينا. إذا رأيت سلكًا مكشوفًا فابتعد عنه وأخبر شخصًا كبيرًا، واترك إصلاح الأعطال للكهربائي المختص.']),
    (1004, 102, 1, 'الثنائي الباعث للضوء (LED)', 'مصباح صغير ملوّن يستهلك طاقة قليلة جدًا.', True, 'lesson-led',
     ['الـ LED اختصار لعبارة Light-Emitting Diode أي الثنائي الباعث للضوء. نراه في شاشات التلفاز وإشارات المرور وأضواء الألعاب، ويستهلك طاقة قليلة جدًا ولا يسخن كثيرًا.',
      'للـ LED رجل طويلة هي الطرف الموجب (الأنود +) ورجل قصيرة هي الطرف السالب (الكاثود −)، لأن الكهرباء تمر فيه في اتجاه واحد فقط. إذا وصّلناه بالعكس لا يضيء.']),
    (1005, 102, 2, 'المقاومة', 'مطبّ صغير يهدّئ الإلكترونات ويحمي المكونات.', True, 'lesson-resistor',
     ['المقاومة تنظّم مرور الكهرباء في الدائرة وتحمي المكونات الحساسة مثل الـ LED من التيار القوي. وتُقاس بوحدة الأوم (Ω)، وكلما زادت قيمتها قلّت حركة الإلكترونات.',
      'الحلقات الملوّنة على جسم المقاومة تحدد قيمتها. ويمكن توصيل المقاومة في أي اتجاه، وهي تحوّل الطاقة الزائدة إلى حرارة خفيفة.']),
    (1006, 102, 3, 'زر الضغط', 'نضغط فتضيء الدائرة، ونرفع أيدينا فتنطفئ.', True, 'lesson-button',
     ['زر الضغط يغلق الدائرة فقط ما دمنا نضغط عليه، وبداخله زنبرك يعيده إلى مكانه بمجرد أن نرفع أيدينا فتنفتح الدائرة وتتوقف الكهرباء.',
      'نستخدم أزرار الضغط في أذرع ألعاب الفيديو ولوحات المفاتيح، وهي توفّر طاقة البطارية لأنها تسمح بمرور الكهرباء عند الحاجة فقط.']),
    (1007, 102, 4, 'المكثف (مسودة)', 'درس قيد الإعداد — غير منشور بعد.', False, 'lesson-capacitor',
     ['المكثف يخزّن الشحنة الكهربية لفترة قصيرة ثم يفرّغها.']),
]

# ---------------------------------------------------------------------------
# Assessment: categories, topics, quizzes, questions
# ---------------------------------------------------------------------------
CATEGORIES = [
    (101, 'أساسيات الكهرباء', 'Electricity basics', 1),
    (102, 'المكونات الإلكترونية', 'Electronic components', 2),
]
TOPICS = [
    # id, ar name, en name, ar description, en description, category, learning level
    (101, 'مفهوم الكهرباء', 'What is electricity', 'الإلكترونات ومصادر الكهرباء وتحوّلها إلى ضوء وحرارة وصوت.', 'Electrons, where electricity comes from, and how it turns into light, heat and sound.', 101, 'Beginner'),
    (102, 'الدائرة الكهربية', 'The electric circuit', 'الدائرة المغلقة والمفتوحة، والبطارية والأسلاك والمفتاح.', 'Closed and open circuits, batteries, wires and switches.', 101, 'Beginner'),
    (103, 'السلامة الكهربية', 'Electrical safety', 'كيف نتعامل مع الكهرباء بأمان في البيت.', 'How to use electricity safely at home.', 101, 'Beginner'),
    (104, 'الثنائي الباعث للضوء', 'The LED', 'كيف يعمل الـ LED ولماذا له رجل طويلة ورجل قصيرة.', 'How an LED works and why it has a long leg and a short leg.', 102, 'Intermediate'),
    (105, 'المقاومة', 'The resistor', 'لماذا نحتاج المقاومة في الدائرة وكيف نقرأ قيمتها.', 'Why a circuit needs a resistor and how to read its value.', 102, 'Intermediate'),
    (106, 'زر الضغط', 'The push button', 'كيف يتحكم زر الضغط في الدائرة ويوفّر طاقة البطارية.', 'How a push button controls a circuit and saves battery power.', 102, 'Intermediate'),
]
# Readable names for the topic ids used below. None = a question that belongs to no topic.
ELECTRICITY, CIRCUIT, SAFETY, LED, RESISTOR, BUTTON = 101, 102, 103, 104, 105, 106
NO_TOPIC = None

TF = lambda correct_is_true: [('صح', 'True', correct_is_true), ('خطأ', 'False', not correct_is_true)]


def mcq(ar, en, topic, diff, options, points=1, image=None, active=True, en_missing=False):
    return dict(type='MultipleChoice', ar=ar, en=en, topic=topic, diff=diff, points=points,
                options=options, image=image, active=active, en_missing=en_missing)


def tf(ar, en, topic, diff, is_true, points=1, image=None):
    return dict(type='TrueFalse', ar=ar, en=en, topic=topic, diff=diff, points=points,
                options=TF(is_true), image=image, active=True, en_missing=False, fixed_order=True)


def essay(ar, en, topic, diff, points):
    return dict(type='Essay', ar=ar, en=en, topic=topic, diff=diff, points=points,
                options=[], image=None, active=True, en_missing=False)


# The curriculum's question bank: every lesson quiz has the lesson's 8 questions and
# every level assessment is that level's final exam, all worth 1 point. Lesson
# questions are Easy, final-exam questions Medium. The review (110) and the
# challenge (111) hold the cases the app must handle that the curriculum does not:
# images, image-only options, essays, 2-3 points, a missing English translation and
# an inactive draft.
# Options: (ar text | None, en text | None, is correct[, (image key, description)]).
QUIZZES = [
    dict(id=101, type='Placement', level=None, lesson=None, active=True,
         ar=('اختبار تحديد المستوى', 'اختبار قصير يحدد المستوى المناسب لك لتبدأ منه.'),
         en=('Placement test', 'A short test that finds the right level for you to start at.'),
         questions=[]),
    dict(id=102, type='LevelAssessment', level=101, lesson=None, active=True,
         ar=('الاختبار النهائي للمستوى الأول', 'اختبر كل ما تعلمته عن الكهرباء والدائرة الكهربية والسلامة.'),
         en=('Level 1 final exam', 'Test everything you learned about electricity, circuits and safety.'),
         questions=[
             mcq('ما اسم الشحنات الصغيرة جدًا التي تتحرك في الأسلاك وتولّد الكهرباء؟', 'What are the tiny charges that flow through wires and generate electricity called?', ELECTRICITY, 'Medium',
                 [('قطرات الماء', 'Water droplets', False), ('الإلكترونات', 'Electrons', True), ('فقاعات الهواء', 'Air bubbles', False)]),
             mcq('من أين تأتي الكهرباء القوية التي تضيء بيوتنا وتشغّل الثلاجة؟', 'Where does the powerful electricity that lights up our homes and powers the refrigerator come from?', ELECTRICITY, 'Medium',
                 [('محطات توليد الكهرباء', 'Power plants', True), ('أشجار الحديقة', 'Trees in the garden', False), ('المخبز', 'The bakery', False)]),
             mcq('عندما نوصّل التلفاز بالكهرباء، تتحول الطاقة الكهربية إلى...', 'When we plug a TV into the electricity, electrical energy transforms into...', ELECTRICITY, 'Medium',
                 [('ضوء وحرارة فقط', 'Light and heat only', False), ('ضوء وصوت', 'Light and sound', True), ('طعام وماء', 'Food and water', False)]),
             mcq('لماذا يُمنع تمامًا لمس المقبس الكهربي بأيدٍ مبللة؟', 'Why is it strictly forbidden to touch an electrical outlet with wet hands?', SAFETY, 'Medium',
                 [('لأن الماء يوصل الكهرباء بسرعة وهذا خطير', 'Because water conducts electricity quickly and is dangerous', True), ('لأن المقبس سيتّسخ', 'Because the outlet will get dirty', False), ('لأن الماء يقطع الكهرباء', 'Because water turns off the electricity', False)]),
             mcq('ما دور البطارية الصغيرة في ألعابنا؟', 'What is the role of the small battery in our toys?', CIRCUIT, 'Medium',
                 [('نفق تسير فيه الكهرباء', 'A tunnel for electricity to travel through', False), ('مصدر الطاقة الذي يدفع الإلكترونات', 'The power source that pushes electrons', True), ('زر لتشغيل اللعبة وإطفائها', 'A button to turn the toy on and off', False)]),
             tf('إذا انقطع السلك في الدائرة الكهربية يظل المصباح مضيئًا بشكل طبيعي.', 'If the wire in an electrical circuit breaks, the light bulb remains lit normally.', CIRCUIT, 'Medium', False),
             mcq('أي مكوّن نستخدمه لفتح الدائرة (إطفائها) أو غلقها (تشغيلها)؟', 'Which component do we use to open the circuit (turn it off) or close it (turn it on)?', CIRCUIT, 'Medium',
                 [('المفتاح الكهربي', 'The electrical switch', True), ('السلك النحاسي', 'The copper wire', False), ('المصباح الزجاجي', 'The glass light bulb', False)]),
             mcq('لماذا تُغطّى الأسلاك الكهربية من الخارج بطبقة من البلاستيك؟', 'Why are electrical wires covered on the outside with a layer of plastic?', SAFETY, 'Medium',
                 [('لحمايتنا، لأن البلاستيك مادة عازلة لا توصل الكهرباء', 'To protect us, because plastic is an insulating material that does not conduct electricity', True), ('فقط لتصبح الأسلاك مرنة', 'Just to make the wires flexible', False), ('لأن البلاستيك يضيء في الظلام', 'Because plastic glows in the dark', False)]),
         ]),
    dict(id=103, type='LevelAssessment', level=102, lesson=None, active=True,
         ar=('الاختبار النهائي للمستوى الثاني', 'هل تعرف الـ LED والمقاومة وزر الضغط جيدًا؟'),
         en=('Level 2 final quiz', 'Do you know the LED, the resistor and the push button well?'),
         questions=[
             mcq('أي مكوّن يضيء بألوان مبهجة ويستهلك طاقة كهربية قليلة جدًا؟', 'Which of the following components lights up in cheerful colors and consumes very little electrical energy?', LED, 'Medium',
                 [('المقاومة', 'Resistor', False), ('الثنائي الباعث للضوء (LED)', 'LED (Light Emitting Diode)', True), ('زر الضغط', 'Push Button', False)]),
             mcq('أي مكوّن يعمل مثل "مطبّ صناعي" يبطئ الإلكترونات ليحمي المكونات الحساسة؟', 'Which component acts like a "speed bump," slowing down electrons to protect delicate parts?', RESISTOR, 'Medium',
                 [('المقاومة', 'Resistor', True), ('البطارية', 'Battery', False), ('السلك النحاسي', 'Copper wire', False)]),
             tf('الرجل الطويلة للـ LED توصَّل دائمًا بالطرف السالب (−) للبطارية.', 'The long leg of an LED always connects to the negative (-) terminal of the battery.', LED, 'Medium', False),
             mcq('يسمح زر الضغط بمرور الكهرباء في الدائرة...', 'A push button allows electricity to flow through the circuit...', BUTTON, 'Medium',
                 [('ما دمنا نضغط عليه', 'As long as we keep pressing it', True), ('بمجرد أن نرفع أيدينا عنه', 'As soon as we lift our hand off it', False), ('تلقائيًا دون ضغط', 'Automatically without pressing', False)]),
             mcq('كيف يعرف المهندسون قيمة المقاومة الكهربية؟', 'How do engineers determine the value and rating of an electrical resistor?', RESISTOR, 'Medium',
                 [('من الحلقات الملوّنة على جسمها', 'By the colored bands on its body', True), ('من وزنها وثقلها', 'By its weight and heaviness', False), ('من طول أرجلها', 'By the length of its leads', False)]),
             tf('يمكن توصيل المقاومة في الدائرة الكهربية في أي اتجاه دون القلق من الطرفين الموجب والسالب.', 'A resistor can be connected in an electrical circuit in either direction without worrying about positive and negative terminals.', RESISTOR, 'Medium', True),
             mcq('لماذا يُفضَّل استخدام زر الضغط في الدوائر الإلكترونية؟', 'Why is it preferable to use a push button in electronic circuits?', BUTTON, 'Medium',
                 [('لأنه يمنع استهلاك طاقة البطارية إلا عند الحاجة', 'Because it prevents battery power consumption except when needed', True), ('لأنه يغيّر لون الضوء', 'Because it changes the color of the light', False), ('لأنه يزيد قوة البطارية', "Because it increases the battery's power", False)]),
             # Draws on all three lessons, so it belongs to no topic: it earns XP but
             # counts toward no topic's statistics.
             mcq('ما المكونات الصحيحة لبناء دائرة آمنة تضيء LED باستخدام زر تحكم؟', 'What are the correct components to build a safe circuit that lights up an LED using a control button?', NO_TOPIC, 'Medium',
                 [('بطارية + مقاومة + زر ضغط + LED', 'Battery + resistor + push-button switch + LED', True), ('بطارية + LED فقط', 'Battery + LED only', False), ('مقاومة + مفتاح بدون بطارية', 'Resistor + switch without a battery', False)]),
         ]),
    dict(id=104, type='LessonQuiz', level=None, lesson=1001, active=True,
         ar=('اختبار درس: ما هي الكهرباء؟', 'ثمانية أسئلة عن الكهرباء ومصادرها وآثارها.'),
         en=('Lesson quiz: What is electricity?', 'Eight questions about electricity, where it comes from and what it does.'),
         questions=[
             mcq('من أين تأتي الكهرباء التي تشغّل ألعابنا؟', 'Where does the electricity that powers our toys come from?', ELECTRICITY, 'Easy',
                 [('من حركة الإلكترونات الصغيرة جدًا', 'The flow of tiny electrons', True), ('من ماء الصنبور', 'Tap water', False), ('من الهواء', 'Air', False)]),
             tf('البرق في السماء نوع من الكهرباء الطبيعية القوية.', 'Lightning in the sky is a type of powerful natural electricity.', ELECTRICITY, 'Easy', True),
             mcq('ماذا نسمي الأشياء التي تحتاج إلى الكهرباء لتعمل؟', 'What do we call things that need electricity to work?', ELECTRICITY, 'Easy',
                 [('الأجهزة الكهربية والإلكترونية', 'Electronic and electrical devices', True), ('الألعاب الخشبية', 'Wooden toys', False), ('الكتب الورقية', 'Paper books', False)]),
             tf('البطارية مصدر صغير وآمن للكهرباء في ألعابنا.', 'A battery is considered a small, safe source of electricity for our toys.', ELECTRICITY, 'Easy', True),
             mcq('ما اسم الشحنات التي تتحرك بسرعة لتصنع الكهرباء؟', 'What are the charges that move quickly to create electricity?', ELECTRICITY, 'Easy',
                 [('الإلكترونات', 'Electrons', True), ('القطرات', 'Droplets', False), ('الكرات', 'Balls', False)]),
             tf('نستطيع أن نرى الكهرباء بأعيننا بوضوح وهي تتحرك داخل الأسلاك.', 'We can clearly see electricity with our eyes as it moves through wires.', ELECTRICITY, 'Easy', False),
             mcq('أين تُنتَج الكهرباء القوية التي تصل إلى بيوتنا؟', 'Where is the powerful electricity that reaches our homes produced?', ELECTRICITY, 'Easy',
                 [('في محطات توليد الكهرباء', 'At power plants', True), ('في المخبز', 'At the bakery', False), ('في الحدائق', 'In parks', False)]),
             mcq('يمكن أن تتحول الطاقة الكهربية إلى...', 'Electrical energy can be converted into...', ELECTRICITY, 'Easy',
                 [('ضوء وحرارة وصوت', 'Light, heat, and sound', True), ('طعام وماء', 'Food and water', False), ('ألعاب بلاستيكية', 'Plastic toys', False)]),
         ]),
    dict(id=105, type='LessonQuiz', level=None, lesson=1002, active=True,
         ar=('اختبار درس: الدائرة الكهربية', 'متى تكون الدائرة مغلقة؟ ومتى تتوقف الكهرباء؟'),
         en=('Lesson quiz: The electric circuit', 'When is a circuit closed, and when does electricity stop?'),
         questions=[
             mcq('ماذا يحتاج المصباح ليضيء؟', 'What does a light bulb need to light up?', CIRCUIT, 'Easy',
                 [('دائرة كهربية مغلقة ومتصلة', 'A closed, connected electric circuit', True), ('سلك مقطوع', 'A broken wire', False), ('دائرة مفتوحة', 'An open circuit', False)]),
             mcq('أيٌّ مما يلي دائرة مفتوحة (لا تمر فيها الكهرباء)؟', 'Which of these is considered an open circuit (where electricity does not flow)?', CIRCUIT, 'Easy',
                 [('دائرة فيها سلك مقطوع', 'A circuit with a broken wire', True), ('دائرة كاملة ومتصلة', 'A complete, connected circuit', False)]),
             mcq('ما وظيفة المفتاح الكهربي؟', 'What is the function of an electric switch?', CIRCUIT, 'Easy',
                 [('فتح الدائرة وغلقها للتحكم في الكهرباء', 'Opening and closing the circuit to control electricity', True), ('إنتاج الطاقة', 'Providing energy', False), ('تغيير لون المصباح', "Changing the light bulb's color", False)]),
             tf('يضيء المصباح حتى لو لم تكن البطارية موجودة في الدائرة.', 'The light bulb lights up even if the battery is not in the circuit.', CIRCUIT, 'Easy', False),
             mcq('ما وظيفة السلك النحاسي في الدائرة الكهربية؟', 'What is the function of the copper wire in an electrical circuit?', CIRCUIT, 'Easy',
                 [('نقل الإلكترونات والكهرباء', 'Transporting electrons and electricity', True), ('إيقاف الكهرباء', 'Stopping electricity', False), ('تبريد البطارية', 'Cooling the battery', False)]),
             tf('عندما تكون الدائرة "مغلقة" يكون الطريق كاملًا وتمر الكهرباء بنجاح.', 'When the circuit is "closed," it means the path is complete and electricity flows successfully.', CIRCUIT, 'Easy', True),
             mcq('ماذا يحدث عندما نطفئ المفتاح الكهربي (OFF)؟', 'What happens when the electrical switch is turned off (OFF)?', CIRCUIT, 'Easy',
                 [('تتوقف الكهرباء وينطفئ المصباح', 'Electricity stops and the light bulb turns off', True), ('يضيء المصباح بقوة', 'The light bulb shines brightly', False), ('تنفجر البطارية', 'The battery explodes', False)]),
             mcq('المكوّن الذي يمدّ الدائرة الكهربية البسيطة بالطاقة هو...', 'The component that provides energy in a simple electrical circuit is:', CIRCUIT, 'Easy',
                 [('البطارية', 'The battery', True), ('المفتاح', 'The switch', False), ('السلك', 'The wire', False)]),
         ]),
    dict(id=106, type='LessonQuiz', level=None, lesson=1003, active=True,
         ar=('اختبار درس: السلامة الكهربية', 'هل تعرف كيف تحمي نفسك من خطر الكهرباء؟'),
         en=('Lesson quiz: Electrical safety', 'Do you know how to stay safe around electricity?'),
         questions=[
             mcq('هل من الآمن لمس المقبس الكهربي بأيدٍ مبللة؟', 'Is it safe to touch an electrical socket with wet hands?', SAFETY, 'Easy',
                 [('لا؛ الماء يوصل الكهرباء وهذا خطير', 'No; water conducts electricity and it is dangerous', True), ('نعم، لا مشكلة', 'Yes, it is fine', False)]),
             mcq('ماذا نفعل إذا وجدنا سلكًا كهربيًا مكشوفًا؟', 'What should we do if we find an exposed electrical wire?', SAFETY, 'Easy',
                 [('نبتعد عنه فورًا ونخبر شخصًا كبيرًا', 'Stay away from it immediately and tell an adult', True), ('نلمسه بأظافرنا', 'Touch it with our fingernails', False), ('نلعب به', 'Play with it', False)]),
             tf('إدخال الأجسام المعدنية، مثل المسمار، في المقبس الكهربي تصرّف آمن.', 'Inserting metal objects, like a nail, into an electrical socket is a safe action.', SAFETY, 'Easy', False),
             mcq('لماذا تُغطّى الأسلاك الكهربية بطبقة من البلاستيك؟', 'Why are electrical wires covered with a layer of plastic?', SAFETY, 'Easy',
                 [('لأن البلاستيك يعزل الكهرباء ويحمينا', 'Because plastic insulates electricity and protects us', True), ('فقط لتصبح ملوّنة', 'Just to make them colorful', False), ('لتصبح أثقل', 'To make them heavier', False)]),
             tf('يجب إطفاء الأجهزة وفصلها عن الكهرباء إذا لم نستخدمها لفترة طويلة.', 'Appliances should be turned off and unplugged when not in use for a long time.', SAFETY, 'Easy', True),
             mcq('عند فصل جهاز عن الكهرباء، يجب أن نمسك...', 'When unplugging a device from the wall, we should hold...', SAFETY, 'Easy',
                 [('القابس البلاستيكي نفسه', 'The plastic plug itself', True), ('السلك ونشدّه بقوة', 'The cord, and pull it forcefully', False), ('الحائط', 'The wall', False)]),
             tf('شدّ السلك بقوة لفصله عن الكهرباء هو التصرف الصحيح.', 'Pulling the cord forcefully to disconnect it from the power is the correct thing to do.', SAFETY, 'Easy', False),
             mcq('من الشخص المناسب لإصلاح الأعطال الكهربية في البيت؟', 'Who is the right person to fix electrical faults at home?', SAFETY, 'Easy',
                 [('كهربائي مختص أو شخص كبير', 'A professional electrician or an adult', True), ('الأطفال الصغار', 'Young children', False), ('لا أحد', 'No one', False)]),
         ]),
    dict(id=107, type='LessonQuiz', level=None, lesson=1004, active=True,
         ar=('اختبار درس: الثنائي الباعث للضوء (LED)', 'كل ما تحتاج معرفته عن الـ LED.'),
         en=('Lesson quiz: The LED (Light-Emitting Diode)', 'Everything you need to know about the LED.'),
         questions=[
             mcq('ماذا يعني الاختصار "LED" في عالم الإلكترونيات؟', 'What does the acronym "LED" stand for in the world of electronics?', LED, 'Easy',
                 [('الثنائي الباعث للضوء (Light-Emitting Diode)', 'Light-Emitting Diode', True), ('جهاز كهربي صغير (Little Electric Device)', 'Little Electric Device', False), ('جهاز كهربي متصل (Linked Electric Device)', 'Linked Electric Device', False)]),
             mcq('لماذا للـ LED رجل طويلة ورجل قصيرة؟', 'Why does an LED have one long leg and one short leg?', LED, 'Easy',
                 [('لأن الكهرباء تمر فيه في اتجاه واحد فقط', 'Because electricity flows through it in only one direction', True), ('ليثبت في الأرض', 'To anchor it into the ground', False), ('إنها مجرد زينة', 'It is just a decorative feature', False)]),
             mcq('ماذا يحدث إذا وصّلنا الـ LED بالعكس في الدائرة الكهربية؟', 'What happens if we connect the LED backwards in an electrical circuit?', LED, 'Easy',
                 [('لن يضيء لأن الكهرباء لا تمر في الاتجاه المعاكس', 'It will not light up because electricity does not flow in reverse', True), ('سيضيء بلون مختلف', 'It will light up in a different color', False), ('سينفجر فورًا', 'It will explode immediately', False)]),
             tf('الـ LED صديق للبيئة لأنه يستهلك طاقة كهربية قليلة جدًا مقارنة بالمصابيح القديمة.', 'The LED is considered eco-friendly because it consumes very little electrical energy compared to older light bulbs.', LED, 'Easy', True),
             mcq('الرجل الطويلة للـ LED تمثّل...', 'The long leg of the LED represents the:', LED, 'Easy',
                 [('الطرف الموجب (الأنود +)', 'Positive terminal (Anode +)', True), ('الطرف السالب (الكاثود −)', 'Negative terminal (Cathode -)', False), ('الطرف المحايد', 'Neutral terminal', False)]),
             tf('الـ LED يصدر حرارة عالية جدًا، وقد تحترق يدك إذا لمسته وهو مضيء.', 'The LED emits very high heat, and your hand would burn if you touched it while it is lit.', LED, 'Easy', False),
             mcq('نرى الـ LED في حياتنا اليومية في...', 'We see LEDs in our daily lives in:', LED, 'Easy',
                 [('شاشات التلفاز وإشارات المرور وأضواء الألعاب', 'TV screens, traffic lights, and toy lights', True), ('الأدوات الخشبية', 'Wooden utensils', False), ('الملابس القطنية', 'Cotton clothing', False)]),
             tf('الـ LED يحتاج إلى طاقة هائلة ليضيء، ولن يعمل أبدًا مع بطارية صغيرة.', 'The LED requires a massive amount of energy to light up and will never work with a small battery.', LED, 'Easy', False),
         ]),
    dict(id=108, type='LessonQuiz', level=None, lesson=1005, active=True,
         ar=('اختبار درس: المقاومة', 'لماذا نحتاج المقاومة؟ وكيف نقرأ قيمتها؟'),
         en=('Lesson quiz: The resistor', 'Why do we need a resistor, and how do we read its value?'),
         questions=[
             mcq('ما الوظيفة الأساسية للمقاومة في الدائرة؟', 'What is the primary function of a resistor in a circuit?', RESISTOR, 'Easy',
                 [('حماية المكونات وتنظيم مرور الكهرباء', 'Protecting components and regulating the flow of electricity', True), ('زيادة القدرة الكهربية', 'Increasing electrical power', False), ('إضاءة الدائرة', 'Illuminating the circuit', False)]),
             mcq('ماذا يحدث للـ LED إذا وصّلناه ببطارية قوية جدًا بدون مقاومة؟', 'What happens to an LED if connected to a very powerful battery without a resistor?', RESISTOR, 'Easy',
                 [('قد يحترق بسبب التيار القوي', 'It may burn out due to the strong current', True), ('سيضيء إلى الأبد', 'It will light up forever', False), ('لن يتأثر', 'It will remain unaffected', False)]),
             mcq('ما فائدة الحلقات الملوّنة المرسومة على جسم المقاومة؟', 'What is the purpose of the colored bands marked on the body of the resistor?', RESISTOR, 'Easy',
                 [('تحديد قيمة المقاومة', "To determine the resistor's value and rating", True), ('لتبدو جميلة', 'To make it look attractive', False), ('لتحديد الطرفين الموجب والسالب', 'To indicate positive and negative terminals', False)]),
             tf('يجب توصيل المقاومة في اتجاه محدد (مع مراعاة الطرفين الموجب والسالب) مثل الـ LED تمامًا.', 'A resistor must be connected in a specific direction (observing positive and negative terminals), just like an LED.', RESISTOR, 'Easy', False),
             mcq('ما وحدة قياس المقاومة الكهربية؟', 'What is the unit of measurement for electrical resistance?', RESISTOR, 'Easy',
                 [('الأوم (Ω)', 'Ohm (Ω)', True), ('الكيلوجرام', 'Kilogram', False), ('المتر', 'Meter', False)]),
             mcq('كلما زادت قيمة المقاومة (بالأوم)، فإن حركة الإلكترونات...', 'As the resistance value (Ohms) increases, the movement of electrons:', RESISTOR, 'Easy',
                 [('تقل وتبطؤ', 'Decreases and slows down', True), ('تزيد وتسرع', 'Increases and speeds up', False), ('تتوقف وتنفجر الدائرة', 'Stops, causing the circuit to explode', False)]),
             mcq('تحوّل المقاومة الطاقة الكهربية الزائدة التي تمتصها إلى...', 'The resistor converts the excess electrical energy it absorbs into:', RESISTOR, 'Easy',
                 [('حرارة خفيفة', 'Mild heat', True), ('ضوء ساطع', 'Bright light', False), ('صوت موسيقي', 'Musical sound', False)]),
             tf('يمكننا استبدال المقاومة بسلك عادي وسيؤدي الوظيفة نفسها تمامًا.', 'We can replace the resistor with an ordinary wire, and it will perform the exact same function.', RESISTOR, 'Easy', False),
         ]),
    dict(id=109, type='LessonQuiz', level=None, lesson=1006, active=True,
         ar=('اختبار درس: زر الضغط', 'كيف يتحكم زر الضغط في الدائرة؟'),
         en=('Lesson quiz: The push button', 'How does a push button control a circuit?'),
         questions=[
             mcq('كيف يعمل زر الضغط؟', 'How does a push button work?', BUTTON, 'Easy',
                 [('يغلق الدائرة فقط عند الضغط عليه', 'It closes the circuit only when pressed', True), ('يظل يعمل إلى الأبد بعد ضغطة واحدة', 'It keeps working forever after a single press', False), ('يولّد الكهرباء بنفسه', 'It generates its own electricity', False)]),
             mcq('ماذا يحدث للدائرة عندما ترفع يدك عن زر الضغط؟', 'What happens to the circuit when you remove your hand from the push button?', BUTTON, 'Easy',
                 [('تنفتح الدائرة وتتوقف الكهرباء', 'The circuit opens and the electricity stops', True), ('تستمر الكهرباء في المرور', 'Electricity continues to flow', False), ('تنفجر البطارية', 'The battery explodes', False)]),
             mcq('نستخدم أزرار الضغط في أجهزة كثيرة حولنا، مثل...', 'We use push buttons in many devices around us, such as...?', BUTTON, 'Easy',
                 [('أذرع ألعاب الفيديو ولوحات المفاتيح', 'Video game controllers and keyboards', True), ('الأسلاك النحاسية', 'Copper wires', False), ('المصابيح الزجاجية', 'Glass light bulbs', False)]),
             tf('يساعدنا المفتاح على توفير طاقة البطارية لأنه يسمح بمرور الكهرباء عند الحاجة فقط.', 'A switch helps us save battery power because it allows electricity to flow only when needed.', BUTTON, 'Easy', True),
             mcq('عندما تضغط على المفتاح، تُسمّى حالة الدائرة...', 'When you press the switch, the circuit state is called:', BUTTON, 'Easy',
                 [('دائرة مغلقة (تشغيل ON)', 'Closed circuit (ON)', True), ('دائرة مفتوحة (إيقاف OFF)', 'Open circuit (OFF)', False), ('دائرة مكسورة', 'Broken circuit', False)]),
             tf('يحتوي زر الضغط على زنبرك داخلي يعيده إلى الأعلى بمجرد أن ترفع يدك.', 'A push button contains an internal spring that pushes it back up as soon as you release your hand.', BUTTON, 'Easy', True),
             tf('زر الضغط العادي يولّد الكهرباء تلقائيًا دون الحاجة إلى بطارية.', 'A standard push button automatically generates electricity without needing a battery.', BUTTON, 'Easy', False),
             mcq('أيٌّ مما يلي يصف القابس أو المفتاح الكهربي بشكل صحيح؟', 'Which of the following correctly describes an electrical plug or switch?', BUTTON, 'Easy',
                 [('أداة للتحكم في أمان الدائرة واتصالها', 'A device to control circuit safety and connectivity', True), ('مكوّن لزيادة حجم السلك', 'A component to increase wire size', False), ('ضوء يلمع بألوان مختلفة', 'A light that shines in different colors', False)]),
         ]),
    dict(id=110, type='LessonReview', level=None, lesson=1002, active=True,
         ar=('مراجعة: الدائرة الكهربية', 'راجع ما تعلمته بسؤال بالصور وسؤال تكتب إجابته بنفسك.'),
         en=('Review: The electric circuit', 'Review what you learned with a picture question and a written answer.'),
         questions=[
             tf('الدائرة الموضحة في الصورة دائرة مغلقة.', 'The circuit in the picture is closed.', CIRCUIT, 'Medium', False,
                image=('q-open-circuit', 'دائرة فيها بطارية على اليسار ومصباح في الأعلى، والسلك السفلي مقطوع من المنتصف، والمصباح مطفأ.')),
             mcq('ما اسم المكوّن الموضح في الصورة؟', 'What is the part shown in the picture?', CIRCUIT, 'Easy',
                 [('بطارية', 'A battery', True), ('مصباح', 'A lamp', False), ('مفتاح', 'A switch', False)],
                 image=('q-battery', 'بطارية أسطوانية خضراء مكتوب على أحد طرفيها علامة + وعلى الطرف الآخر علامة −.')),
             # No English translation on purpose: an "en" request falls back to Arabic
             # and the response says languageFallbackApplied = true.
             mcq('ماذا يحدث للمصباح إذا فتحنا المفتاح؟', None, CIRCUIT, 'Medium',
                 [('ينطفئ', None, True), ('يزداد ضوءه', None, False), ('لا يتغير', None, False)], points=2, en_missing=True),
             essay('صف ماذا يحدث للمصباح عندما نفتح المفتاح ثم نغلقه.', 'Describe what happens to the lamp when we open the switch and then close it.', CIRCUIT, 'Medium', 2),
         ]),
    dict(id=111, type='Standalone', level=None, lesson=None, active=True,
         ar=('تحدي الكهرباء', 'أسئلة متنوعة بالصور وسؤال تكتب إجابته، لكل المستويات.'),
         en=('Electricity challenge', 'Mixed questions with pictures and a written answer, for every level.'),
         questions=[
             mcq('أي صورة تُظهر مادة موصلة للكهرباء؟', 'Which picture shows a material that conducts electricity?', SAFETY, 'Medium',
                 [(None, None, True, ('opt-copper', 'سلك نحاسي لامع ملفوف على شكل حلقات.')),
                  (None, None, False, ('opt-rubber', 'قطعة مطاط سوداء مستطيلة.')),
                  (None, None, False, ('opt-wood', 'قطعة خشب جاف بلون بني فاتح.'))], points=2),
             mcq('أيٌّ من هذه يحتاج إلى كهرباء ليعمل؟', 'Which of these needs electricity to work?', ELECTRICITY, 'Easy',
                 [('مصباح', 'A lamp', True, ('opt-bulb', 'مصباح كهربي مضيء باللون الأصفر.')), ('كتاب', 'A book', False), ('كرة', 'A ball', False)]),
             tf('ماء الصنبور يمكن أن يوصل الكهرباء.', 'Tap water can conduct electricity.', SAFETY, 'Hard', True),
             essay('اشرح بكلماتك لماذا يجب ألا نلمس الأسلاك المكشوفة.', 'Explain in your own words why we must not touch bare wires.', SAFETY, 'Medium', 3),
             # A draft the admin has not published: never served to a child.
             mcq('ما لون ضوء الـ LED؟ (مسودة)', 'What colour is the light of an LED? (draft)', LED, 'Easy',
                 [('يمكن أن يكون بألوان مختلفة', 'It can be many different colours', True), ('أحمر فقط', 'Only red', False)], active=False),
         ]),
]

# ---------------------------------------------------------------------------
# Build the question / option rows with ids
# ---------------------------------------------------------------------------
questions = []   # dicts with id, quiz, display, ... options list of dicts
options_by_id = {}
qid, oid = 1001, 10001
for quiz in QUIZZES:
    for display, q in enumerate(quiz['questions'], start=1):
        row = dict(q, id=qid, quiz=quiz['id'], display=display)
        raw = list(q['options'])
        # Vary where the correct answer sits, so it is not always the first one.
        if raw and not q.get('fixed_order'):
            k = qid % len(raw)
            raw = raw[k:] + raw[:k]
        opts = []
        for od, o in enumerate(raw, start=1):
            image = o[3] if len(o) > 3 else None
            opt = dict(id=oid, question=qid, ar=o[0], en=o[1], correct=o[2], display=od, image=image)
            opts.append(opt)
            options_by_id[oid] = opt
            oid += 1
        row['options'] = opts
        questions.append(row)
        qid += 1
Q_BY_ID = {q['id']: q for q in questions}
Q_BY_KEY = {(q['quiz'], q['display']): q for q in questions}
QUIZ_BY_ID = {z['id']: z for z in QUIZZES}

# ---- validate authoring rules (mirrors the schema and QuestionService.EnsureAnswerableAsync)
for q in questions:
    n, c = len(q['options']), sum(o['correct'] for o in q['options'])
    if q['type'] == 'Essay':
        assert n == 0, q
    elif q['type'] == 'TrueFalse':
        assert n == 2 and c == 1, q
    else:
        assert n >= 2 and c == 1, q
    assert q['points'] > 0
    if q['image']:
        assert q['image'][1].strip() and len(q['image'][1]) <= 1000
    for o in q['options']:
        assert o['ar'] is not None or o['image'] is not None, o
        if o['image']:
            assert o['image'][1].strip() and len(o['image'][1]) <= 1000
    assert q['diff'] in ('Easy', 'Medium', 'Hard', 'Advanced')
    assert q['topic'] is None or q['topic'] in {t[0] for t in TOPICS}
assert len({(q['quiz'], q['display']) for q in questions}) == len(questions)
for z in QUIZZES:
    t = z['type']
    assert (t == 'LevelAssessment' and z['level'] and not z['lesson']) or \
           (t in ('LessonQuiz', 'LessonReview') and z['lesson'] and not z['level']) or \
           (t in ('Standalone', 'Placement') and not z['level'] and not z['lesson']), z
lesson_quiz_lessons = [z['lesson'] for z in QUIZZES if z['type'] == 'LessonQuiz' and z['active']]
assert len(lesson_quiz_lessons) == len(set(lesson_quiz_lessons))
assert qid <= 1100 and oid <= 11000


def correct_option(q):
    return next(o for o in q['options'] if o['correct'])


def wrong_option(q):
    return next(o for o in q['options'] if not o['correct'])


def placement_question_ids():
    """PlacementEngine.SelectQuestionIdsAsync: per level in Order, the level's newest
    active LevelAssessment quiz, its first N active auto-graded answerable questions."""
    ids = []
    for level_id, _, _ in LEVELS:
        quiz_ids = [z['id'] for z in QUIZZES if z['type'] == 'LevelAssessment' and z['active'] and z['level'] == level_id]
        if not quiz_ids:
            continue
        quiz_id = max(quiz_ids)
        pool = sorted((q for q in questions if q['quiz'] == quiz_id and q['active'] and q['type'] != 'Essay'),
                      key=lambda q: (q['display'], q['id']))
        ids += [q['id'] for q in pool[:PLACEMENT_QUESTIONS_PER_LEVEL]]
    return ids


CONTENT_BLOCKS = sum(3 if len(l[7]) > 1 else 2 for l in LESSONS)
QUIZ_COUNT = lambda quiz_type: sum(1 for z in QUIZZES if z['type'] == quiz_type)


def pct(earned, total):
    if total == 0:
        return Decimal('0.00')
    return (Decimal(earned) * 100 / Decimal(total)).quantize(Decimal('0.01'), rounding=ROUND_HALF_UP)


# ---------------------------------------------------------------------------
# Attempts (history for the demo children)
# ---------------------------------------------------------------------------
DAY = 1440
ATTEMPTS = [
    # Omar: placed at level 2; a review with a graded essay, Hint-button and post-submit
    # hints, and its finished retry; an LED quiz with 2 wrong answers still waiting for a
    # retry; a perfect circuit quiz; and "What is electricity" mastered.
    dict(id=100001, user='omar', quiz=101, started=20 * DAY, duration=14,
         wrong={(102, 3), (103, 1), (103, 3)}),
    dict(id=100002, user='omar', quiz=110, started=18 * DAY, duration=9, wrong={(110, 2)},
         essays={(110, 4): dict(text='لما بنفتح المفتاح اللمبة بتطفي عشان الكهربا اتقطعت، ولما نقفله بتنور تاني.', state='Graded', points=2,
                               feedback='إجابة رائعة! عندما نفتح المفتاح تنقطع الدائرة فينطفئ المصباح، وعندما نغلقه تكتمل الدائرة فيضيء من جديد.',
                               confidence='0.91')},
         hints=[((110, 2), 'button', 'انظر إلى العلامتين + و − على طرفي هذا المكوّن: ما الذي يمدّ الدائرة بالطاقة؟'),
                ((110, 2), 'post', 'هذا المكوّن مخزن صغير للطاقة نضعه في الألعاب وجهاز التحكم، وله طرف موجب وطرف سالب.')]),
    dict(id=100003, user='omar', quiz=110, previous=100002, started=18 * DAY - 30, duration=2, wrong=set()),
    dict(id=100004, user='omar', quiz=107, started=10 * DAY, duration=7, wrong={(107, 3), (107, 6)},
         hints=[((107, 3), 'button', 'فكّر في الشارع ذي الاتجاه الواحد: هل تستطيع السيارات أن تسير فيه بالعكس؟'),
                ((107, 3), 'button', 'تذكّر أن الكهرباء في الـ LED تسير في اتجاه واحد فقط، من الرجل الطويلة إلى الرجل القصيرة.'),
                ((107, 3), 'post', 'جرّب أن تتخيل ماء يحاول أن يصعد في منزلق مائي من الأسفل إلى الأعلى.'),
                ((107, 6), 'post', 'فكّر: هل تشعر بسخونة عندما تلمس أضواء لعبتك المضيئة؟')]),
    dict(id=100005, user='omar', quiz=105, started=5 * DAY, duration=6, wrong=set()),
    # Salma: placed at level 2 with both levels mastered; one essay still waiting for
    # the AI, one the AI declined; the resistor mastered; XP on a question with no topic.
    dict(id=100006, user='salma', quiz=101, started=15 * DAY, duration=11,
         wrong={(102, 2), (103, 4)}),
    dict(id=100007, user='salma', quiz=110, started=3 * DAY, duration=10, wrong={(110, 1)},
         essays={(110, 4): dict(text='لو فتحنا المفتاح النور بيقفل، ولو قفلناه بينور تاني عشان الدايرة بتكمل.', state='Pending')},
         hints=[((110, 1), 'post', 'انظر جيدًا إلى السلك في أسفل الصورة: هل الطريق متصل من أوله إلى آخره؟')]),
    dict(id=100008, user='salma', quiz=111, started=1 * DAY, duration=5, wrong=set(),
         essays={(111, 4): dict(text='مش عارفة', state='NotGraded')}),
    # Laila: never placed but has history, so placement is Optional; a retry is available.
    dict(id=100009, user='laila', quiz=111, started=2 * DAY, duration=4, wrong={(111, 3)},
         essays={(111, 4): dict(text='عشان ممكن تكهربنا.', state='Graded', points=1,
                               feedback='صحيح، السلك المكشوف قد يسبب صدمة كهربية. أضف ماذا يجب أن نفعل عندما نراه: نبتعد عنه ونخبر شخصًا كبيرًا.',
                               confidence='0.84')},
         hints=[((111, 3), 'post', 'فكّر في الفرق بين الماء المقطّر وماء الصنبور الذي تذوب فيه أملاح.')]),
    dict(id=100010, user='omar', quiz=104, started=8 * DAY, duration=5, wrong={(104, 6)},
         hints=[((104, 6), 'post', 'فكّر: عندما يضيء المصباح، هل نرى ما يتحرك داخل السلك أم نرى الضوء فقط؟')]),
    dict(id=100011, user='salma', quiz=108, started=6 * DAY, duration=6, wrong={(108, 4)},
         hints=[((108, 4), 'post', 'قارن بين المقاومة والـ LED: أيهما له رجل طويلة ورجل قصيرة؟')]),
    dict(id=100012, user='salma', quiz=103, started=4 * DAY, duration=8, wrong={(103, 3)}),
]

attempt_rows, aq_rows, mistake_rows, essay_rows, hint_rows, placement_rows = [], [], [], [], [], []
mistake_id = 100001
by_id = {}
for a in ATTEMPTS:
    quiz = QUIZ_BY_ID[a['quiz']]
    if quiz['type'] == 'Placement':
        asked = [Q_BY_ID[i] for i in placement_question_ids()]
    elif a.get('previous'):
        prev = by_id[a['previous']]
        assert prev['quiz'] == a['quiz'] and prev['user'] == a['user']
        asked = [Q_BY_ID[i] for i in prev['wrong_ids'] if Q_BY_ID[i]['active']]
        assert asked
    else:
        asked = sorted((q for q in questions if q['quiz'] == a['quiz'] and q['active']), key=lambda q: q['display'])
    wrong_ids = {Q_BY_KEY[k]['id'] for k in a['wrong']}
    assert wrong_ids <= {q['id'] for q in asked}
    auto = [q for q in asked if q['type'] != 'Essay']
    essays_asked = [q for q in asked if q['type'] == 'Essay']
    assert all(q['type'] != 'Essay' for q in (Q_BY_ID[i] for i in wrong_ids))
    essay_spec = {Q_BY_KEY[k]['id']: v for k, v in a.get('essays', {}).items()}
    assert set(essay_spec) == {q['id'] for q in essays_asked}, (a['id'], essay_spec, essays_asked)

    correct_count = len(auto) - len(wrong_ids)
    auto_total = sum(q['points'] for q in auto)
    auto_earned = sum(q['points'] for q in auto if q['id'] not in wrong_ids)
    score = pct(auto_earned, auto_total)
    started, completed = a['started'], a['started'] - a['duration']
    assert completed >= 0 and a['started'] > completed
    attempt_rows.append(dict(id=a['id'], quiz=a['quiz'], user=USER_ID[a['user']], total=len(asked),
                             correct=correct_count, score=score, started=started, completed=completed,
                             previous=a.get('previous')))
    for q in asked:
        aq_rows.append(dict(attempt=a['id'], question=q['id'], topic=q['topic'], diff=q['diff'],
                            correct=None if q['type'] == 'Essay' else correct_option(q)['id'],
                            type=q['type'], points=q['points'], created=started))
    mistakes_here = {}
    for q in auto:
        if q['id'] in wrong_ids:
            mistake_rows.append(dict(id=mistake_id, attempt=a['id'], question=q['id'],
                                     selected=wrong_option(q)['id'], created=completed))
            mistakes_here[q['id']] = mistake_id
            mistake_id += 1
    for q in essays_asked:
        spec = essay_spec[q['id']]
        row = dict(attempt=a['id'], question=q['id'], text=spec['text'], status=spec['state'], max=q['points'],
                   created=completed, awarded=None, feedback=None, graded_by=None, graded_at=None,
                   outcome=None, ai_attempts=0, ai_last=None, confidence=None)
        if spec['state'] == 'Graded':
            assert 0 <= spec['points'] <= q['points']
            row.update(awarded=spec['points'], feedback=spec['feedback'], graded_by='Ai', graded_at=completed - 1,
                       outcome='Accepted', ai_attempts=1, ai_last=completed - 1, confidence=spec['confidence'])
        elif spec['state'] == 'NotGraded':
            row.update(outcome='Declined', ai_attempts=1, ai_last=completed - 1)
        essay_rows.append(row)
    seq = defaultdict(int)
    for key, kind, text in a.get('hints', []):
        q = Q_BY_KEY[key]
        assert q['id'] in {x['id'] for x in asked}
        seq[q['id']] += 1
        button_level = sum(1 for k2, kind2, _ in a['hints'][:a['hints'].index((key, kind, text)) + 1]
                           if Q_BY_KEY[k2]['id'] == q['id'] and kind2 == 'button')
        hint_rows.append(dict(attempt=a['id'], question=q['id'],
                              mistake=mistakes_here[q['id']] if kind == 'post' else None,
                              text=text, seq=seq[q['id']], attempt_number=button_level if kind == 'button' else None,
                              generated=(started - 2) if kind == 'button' else completed))
        if kind == 'post':
            assert q['id'] in mistakes_here
        # A hint must never contain the answer's wording (HintSafety, simplified).
        answer = correct_option(q)['ar'] if q['type'] != 'Essay' else None
        if answer and q['type'] == 'MultipleChoice':
            assert answer not in text, (text, answer)
    if quiz['type'] == 'Placement':
        # PlacementEngine.Decide: first level not mastered; all mastered → the last.
        placed = None
        for level_id, _, _ in LEVELS:
            level_quiz = max(z['id'] for z in QUIZZES if z['type'] == 'LevelAssessment' and z['level'] == level_id)
            level_qs = [q for q in auto if q['quiz'] == level_quiz]
            earned = sum(q['points'] for q in level_qs if q['id'] not in wrong_ids)
            total = sum(q['points'] for q in level_qs)
            if pct(earned, total) < PASS_PERCENTAGE:
                placed = level_id
                break
        placed = placed or LEVELS[-1][0]
        placement_rows.append(dict(user=USER_ID[a['user']], attempt=a['id'], level=placed, score=score, placed=completed))
    by_id[a['id']] = dict(a, wrong_ids=sorted(wrong_ids))

# Retry uniqueness and placement uniqueness.
prevs = [r['previous'] for r in attempt_rows if r['previous']]
assert len(prevs) == len(set(prevs))
assert len({p['user'] for p in placement_rows}) == len(placement_rows)
# Omar masters level 1 but not level 2; Salma masters both and is placed at the last level.
assert placement_rows[0]['level'] == 102 and placement_rows[1]['level'] == 102
assert len({(h['attempt'], h['question'], h['seq']) for h in hint_rows}) == len(hint_rows)
# UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber: each Hint-button level is saved once.
button_levels = [(h['attempt'], h['question'], h['attempt_number']) for h in hint_rows if h['attempt_number'] is not None]
assert len(set(button_levels)) == len(button_levels)

# UserTopicStats exactly as UserTopicStatService builds them, oldest attempt first.
# A question with no topic counts toward no topic (it still earns XP, below).
stats = {}
for r in sorted(attempt_rows, key=lambda r: -r['completed']):
    wrong = {m['question'] for m in mistake_rows if m['attempt'] == r['id']}
    for aq in (x for x in aq_rows if x['attempt'] == r['id'] and x['type'] != 'Essay' and x['topic'] is not None):
        key = (r['user'], aq['topic'], aq['diff'])
        s = stats.setdefault(key, dict(answered=0, correct=0, last=None, practiced=None))
        s['answered'] += 1
        s['correct'] += 0 if aq['question'] in wrong else 1
        s['last'], s['practiced'] = r['id'], r['completed']
aq_index = {(x['attempt'], x['question']): x for x in aq_rows}
user_of_attempt = {r['id']: r['user'] for r in attempt_rows}
for s in stats.values():
    s['hints'] = 0
for h in hint_rows:
    aq = aq_index[(h['attempt'], h['question'])]
    key = (user_of_attempt[h['attempt']], aq['topic'], aq['diff'])
    if key in stats:
        stats[key]['hints'] += 1
for s in stats.values():
    assert 0 <= s['correct'] <= s['answered']

# XP as UserTopicStatService.LoadXpAsync reads it: each correct MultipleChoice/TrueFalse
# answer at its snapshot Points, plus each graded essay's awarded points, every attempt.
wrong_keys = {(m['attempt'], m['question']) for m in mistake_rows}
xp = defaultdict(int)
for x in aq_rows:
    if x['type'] != 'Essay' and (x['attempt'], x['question']) not in wrong_keys:
        xp[user_of_attempt[x['attempt']]] += x['points']
for e in essay_rows:
    if e['status'] == 'Graded':
        xp[user_of_attempt[e['attempt']]] += e['awarded']


def stats_of_user(user_id):
    return sum(1 for (u, _, _) in stats if u == user_id)


def mastered_topics(user_id, percentage=80, min_questions=5):
    """TopicProgress.Mastery with the default Assessment settings."""
    totals = defaultdict(lambda: [0, 0])
    for (u, topic, _), s in stats.items():
        if u == user_id:
            totals[topic][0] += s['answered']
            totals[topic][1] += s['correct']
    return sorted(t for t, (a, c) in totals.items() if a >= min_questions and c * 100 >= percentage * a)

# ---------------------------------------------------------------------------
# SQL helpers
# ---------------------------------------------------------------------------


def lit(v):
    if v is None:
        return 'NULL'
    if isinstance(v, bool):
        return '1' if v else '0'
    if isinstance(v, (int, Decimal)):
        return str(v)
    return "N'" + str(v).replace("'", "''") + "'"


def ago(minutes):
    return f'DATEADD(MINUTE, -{minutes}, @Now)'


def image_url(key):
    return f'/uploads/lessons/seed-{key}.png'


def insert(table, columns, rows, identity=False):
    out = []
    if not rows:
        return ''
    if identity:
        out.append(f'SET IDENTITY_INSERT {table} ON;')
    for i in range(0, len(rows), 200):
        chunk = rows[i:i + 200]
        out.append(f'INSERT INTO {table} ({", ".join(columns)})\nVALUES')
        out.append(',\n'.join('    (' + ', '.join(r) + ')' for r in chunk) + ';')
    if identity:
        out.append(f'SET IDENTITY_INSERT {table} OFF;')
    return '\n'.join(out) + '\n'


sql = []
w = sql.append

w(f"""/* ============================================================================
   seed_dev_data.sql — fake data for development and frontend work
   ----------------------------------------------------------------------------
   GENERATED FILE. Fills every table the app reads with realistic Arabic/English
   content so the Child app, the Parent app and the Admin dashboard have data.

   Prerequisites (database VoltDB):
     * Users.* and LearningContent.* tables exist (the Users and Content modules).
     * The Assessment schema is current: db/migrations/000_AssessmentSchema.sql
       for an empty database, or 001_points_ai_essays_image_descriptions.sql then
       002_optional_topics_hint_level_uniqueness.sql for one built from an earlier 000.
     * The placeholder images live in ElectroWorld/wwwroot/uploads/lessons/seed-*.png
       (committed with this script).

   Accounts (log in with POST /api/auth/login):
     Admin     admin@volt.dev            {ADMIN_PASSWORD}
     Parent    mona.parent@volt.dev      {DEMO_PASSWORD}   (children: Omar, Salma)
     Parent    khaled.parent@volt.dev    {DEMO_PASSWORD}   (child: Youssef)
     Child     omar@volt.dev             {DEMO_PASSWORD}   placed at level 2, quiz history, hints, retry available
     Child     salma@volt.dev            {DEMO_PASSWORD}   placed at level 2 (both mastered), one essay Pending, one NotGraded
     Child     youssef@volt.dev          {DEMO_PASSWORD}   brand new: placement Required
     Child     laila@volt.dev            {DEMO_PASSWORD}   not placed but has history: placement Optional
     Child     disabled.child@volt.dev   {DEMO_PASSWORD}   deactivated: login is refused
   These passwords are for development only. Never run this script in production.

   What it creates
     Users            9 users, 3 parent-child links (no refresh tokens or reset
                      codes: those are created by logging in / forgot-password)
     Content          {len(LEVELS)} levels, {len(LESSONS)} lessons ({sum(1 for l in LESSONS if l[5])} published, {sum(1 for l in LESSONS if not l[5])} draft), {CONTENT_BLOCKS} content blocks,
                      the content types Text / Image / TextAndImage if missing
     Assessment       {len(CATEGORIES)} categories, {len(TOPICS)} topics (ar + en), {len(QUIZZES)} quizzes: 1 placement,
                      {QUIZ_COUNT('LevelAssessment')} level final exams, {QUIZ_COUNT('LessonQuiz')} lesson quizzes, 1 lesson review,
                      1 standalone; {len(questions)} questions and {len(options_by_id)} options with
                      ar + en translations, images with descriptions, points 1–3,
                      essays, one question with no topic, and one inactive draft question
     History          {len(attempt_rows)} completed attempts, {len(mistake_rows)} wrong answers, {len(essay_rows)} essay answers,
                      {len(hint_rows)} hints, {len(placement_rows)} placements, {len(stats)} topic statistics rows

   Safety
     * Fixed ids in reserved ranges (levels/categories/topics/quizzes 101+, lessons
       and questions 1001+, content blocks and options 10001+, attempts 100001+,
       users 5EED0000-…), so it can sit next to data you already have.
     * Runs once: if the seed admin already exists it prints a message and stops.
     * All-or-nothing: one transaction. If any seed id, email or name is already
       taken by other data, it stops before writing and lists the conflicts.
     * db/seeds/remove_seed_data.sql removes everything this script added.

   Run: sqlcmd -S <server> -d VoltDB -b -i db/seeds/seed_dev_data.sql
   ========================================================================== */

USE VoltDB;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET NOCOUNT ON;
GO

/* One batch from here to the end, so the checks below can stop the whole run. */

IF OBJECT_ID(N'Users.Users') IS NULL OR OBJECT_ID(N'Users.ParentChildLinks') IS NULL
   OR OBJECT_ID(N'LearningContent.Levels') IS NULL OR OBJECT_ID(N'LearningContent.Lessons') IS NULL
   OR OBJECT_ID(N'LearningContent.LessonContents') IS NULL OR OBJECT_ID(N'LearningContent.ContentTypes') IS NULL
   OR OBJECT_ID(N'Assessment.Quizzes') IS NULL OR OBJECT_ID(N'Assessment.UserPlacements') IS NULL
    THROW 50100, N'seed: a Users, LearningContent or Assessment table is missing. Create the schema first.', 1;

IF COL_LENGTH(N'Assessment.QuizAttemptQuestions', N'Points') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.check_constraints
                  WHERE name = N'CK_QuizAttemptEssayAnswers_Status' AND definition LIKE N'%NotGraded%')
    THROW 50101, N'seed: the Assessment schema is out of date. Run db/migrations/001_points_ai_essays_image_descriptions.sql first.', 1;

IF COLUMNPROPERTY(OBJECT_ID(N'Assessment.Questions'), N'TopicId', 'AllowsNull') = 0
   OR NOT EXISTS (SELECT 1 FROM sys.indexes
                  WHERE object_id = OBJECT_ID(N'Assessment.QuestionHints')
                    AND name = N'UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber')
    THROW 50103, N'seed: the Assessment schema is out of date. Run db/migrations/002_optional_topics_hint_level_uniqueness.sql first.', 1;

IF EXISTS (SELECT 1 FROM Users.Users WHERE Id = '{USER_ID['admin']}')
BEGIN
    PRINT N'seed: the development data is already there (admin@volt.dev exists). Nothing to do.';
    RETURN;
END

/* ---- Conflicts: anything in the seed's id ranges or with its unique names ---- */
DECLARE @Conflicts NVARCHAR(MAX) = N'';

SELECT @Conflicts += N'Users.Users: ' + ISNULL(Email, CONVERT(NVARCHAR(36), Id)) + NCHAR(10)
FROM Users.Users
WHERE Id IN ({', '.join("'" + u[1] + "'" for u in USERS)})
   OR Email IN ({', '.join(lit(u[2]) for u in USERS if u[2])});

SELECT @Conflicts += N'LearningContent.Levels id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM LearningContent.Levels WHERE Id BETWEEN 101 AND 199;
SELECT @Conflicts += N'LearningContent.Lessons id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM LearningContent.Lessons WHERE Id BETWEEN 1001 AND 1099;
SELECT @Conflicts += N'LearningContent.LessonContents id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM LearningContent.LessonContents WHERE Id BETWEEN 10001 AND 10999;
SELECT @Conflicts += N'Assessment.Categories: ' + Name + NCHAR(10)
FROM Assessment.Categories WHERE Id BETWEEN 101 AND 199 OR Name IN ({', '.join(lit(c[1]) for c in CATEGORIES)});
SELECT @Conflicts += N'Assessment.Topics: ' + Name + NCHAR(10)
FROM Assessment.Topics WHERE Id BETWEEN 101 AND 199 OR Name IN ({', '.join(lit(t[1]) for t in TOPICS)});
SELECT @Conflicts += N'Assessment.Quizzes id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM Assessment.Quizzes WHERE Id BETWEEN 101 AND 199;
SELECT @Conflicts += N'Assessment.Questions id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM Assessment.Questions WHERE Id BETWEEN 1001 AND 1099;
SELECT @Conflicts += N'Assessment.QuestionOptions id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM Assessment.QuestionOptions WHERE Id BETWEEN 10001 AND 10999;
SELECT @Conflicts += N'Assessment.QuizAttempts id ' + CONVERT(NVARCHAR(20), Id) + NCHAR(10)
FROM Assessment.QuizAttempts WHERE Id BETWEEN 100001 AND 100999;
SELECT @Conflicts += N'Assessment.QuizAttemptMistakes id ' + CONVERT(NVARCHAR(20), Id) + NCHAR(10)
FROM Assessment.QuizAttemptMistakes WHERE Id BETWEEN 100001 AND 100999;

IF @Conflicts <> N''
BEGIN
    DECLARE @Message NVARCHAR(2047) = LEFT(N'seed: stopped before writing anything; these rows already exist:' + NCHAR(10) + @Conflicts, 2047);
    THROW 50102, @Message, 1;
END

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
""")

# ---- Users
w('/* ============================== Users ============================== */')
w(insert('Users.Users',
         ['Id', 'Email', 'PasswordHash', 'FullName', 'Role', 'AuthProvider', 'ProviderUserId', 'Age', 'IsActive', 'ConvertedFromGuestAt', 'CreatedAt'],
         [["'" + u[1] + "'", lit(u[2]), lit(u[3]), lit(u[4]), lit(u[5]), lit(u[6]), 'NULL', lit(u[7]), lit(u[8]), 'NULL', ago(u[9] * DAY)] for u in USERS]))
w(insert('Users.ParentChildLinks', ['Id', 'ParentUserId', 'ChildUserId', 'CreatedAt'],
         [["'" + guid(n) + "'", "'" + USER_ID[p] + "'", "'" + USER_ID[c] + "'", ago(38 * DAY)] for n, p, c in PARENT_LINKS]))

# ---- Content
w('/* ============================== Content ============================== */')
w("""-- Content types are shared reference data: added only when missing, found by name.
INSERT INTO LearningContent.ContentTypes (Name)
SELECT v.Name FROM (VALUES (N'Text'), (N'Image'), (N'TextAndImage')) AS v (Name)
WHERE NOT EXISTS (SELECT 1 FROM LearningContent.ContentTypes AS ct WHERE ct.Name = v.Name);

DECLARE @CtText INT = (SELECT Id FROM LearningContent.ContentTypes WHERE Name = N'Text');
DECLARE @CtImage INT = (SELECT Id FROM LearningContent.ContentTypes WHERE Name = N'Image');
DECLARE @CtTextAndImage INT = (SELECT Id FROM LearningContent.ContentTypes WHERE Name = N'TextAndImage');

-- Seed levels go after any level that already exists.
DECLARE @LevelOrderBase INT = ISNULL((SELECT MAX([Order]) FROM LearningContent.Levels), 0);
""")
w(insert('LearningContent.Levels', ['Id', 'Title', 'Description', '[Order]'],
         [[str(l[0]), lit(l[1]), lit(l[2]), f'@LevelOrderBase + {i}'] for i, l in enumerate(LEVELS, start=1)], identity=True))
w(insert('LearningContent.Lessons', ['Id', 'LevelId', 'Title', 'Description', 'SortOrder', 'IsPublished', 'CreatedAt'],
         [[str(l[0]), str(l[1]), lit(l[3]), lit(l[4]), str(l[2]), lit(l[5]), ago((50 - i) * DAY)] for i, l in enumerate(LESSONS)], identity=True))
content_rows, cid = [], 10001
for l in LESSONS:
    texts, sort = l[7], 1
    content_rows.append([str(cid), str(l[0]), '@CtText', lit(texts[0]), 'NULL', str(sort)]); cid += 1; sort += 1
    content_rows.append([str(cid), str(l[0]), '@CtImage', 'NULL', lit(image_url(l[6])), str(sort)]); cid += 1; sort += 1
    if len(texts) > 1:
        content_rows.append([str(cid), str(l[0]), '@CtTextAndImage', lit(texts[1]), lit(image_url(l[6])), str(sort)]); cid += 1
w(insert('LearningContent.LessonContents', ['Id', 'LessonId', 'ContentTypeId', 'Content', 'MediaUrl', 'SortOrder'], content_rows, identity=True))
assert len(content_rows) == CONTENT_BLOCKS

# ---- Assessment reference data
w('/* ============================== Assessment: classification ============================== */')
w("""INSERT INTO Assessment.Languages (Code, Name)
SELECT v.Code, v.Name FROM (VALUES (N'ar', N'Arabic'), (N'en', N'English')) AS v (Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Assessment.Languages AS l WHERE l.Code = v.Code);
""")
w(insert('Assessment.Categories', ['Id', 'Name', 'SortOrder', 'IsActive'],
         [[str(c[0]), lit(c[1]), str(c[3]), '1'] for c in CATEGORIES]))
w(insert('Assessment.CategoryTranslations', ['CategoryId', 'LanguageCode', 'Name'],
         [r for c in CATEGORIES for r in ([str(c[0]), "N'ar'", lit(c[1])], [str(c[0]), "N'en'", lit(c[2])])]))
w(insert('Assessment.Topics', ['Id', 'Name', 'Description', 'CategoryId', 'LearningLevel', 'IsActive', 'CreatedAt'],
         [[str(t[0]), lit(t[1]), lit(t[3]), str(t[5]), lit(t[6]), '1', ago(55 * DAY)] for t in TOPICS], identity=True))
w(insert('Assessment.TopicTranslations', ['TopicId', 'LanguageCode', 'Name', 'Description'],
         [r for t in TOPICS for r in ([str(t[0]), "N'ar'", lit(t[1]), lit(t[3])], [str(t[0]), "N'en'", lit(t[2]), lit(t[4])])]))

# ---- Quizzes and questions
w('/* ============================== Assessment: quizzes and questions ============================== */')
w("""-- Only one placement test may be active. If one already exists, the seed's copy is
-- added inactive and the existing one keeps serving (it samples every level anyway).
DECLARE @PlacementActive BIT = CASE WHEN EXISTS (SELECT 1 FROM Assessment.Quizzes
                                                 WHERE QuizType = N'Placement' AND IsActive = 1) THEN 0 ELSE 1 END;
""")
w(insert('Assessment.Quizzes', ['Id', 'Title', 'Description', 'QuizType', 'LevelId', 'LessonId', 'IsActive', 'CreatedAt'],
         [[str(z['id']), lit(z['ar'][0]), lit(z['ar'][1]), lit(z['type']), lit(z['level']), lit(z['lesson']),
           '@PlacementActive' if z['type'] == 'Placement' else lit(z['active']), ago(52 * DAY)] for z in QUIZZES], identity=True))
w(insert('Assessment.QuizTranslations', ['QuizId', 'LanguageCode', 'Title', 'Description'],
         [r for z in QUIZZES for r in ([str(z['id']), "N'ar'", lit(z['ar'][0]), lit(z['ar'][1])],
                                       [str(z['id']), "N'en'", lit(z['en'][0]), lit(z['en'][1])])]))
w(insert('Assessment.Questions', ['Id', 'QuizId', 'TopicId', 'QuestionText', 'QuestionType', 'ImageUrl', 'ImageDescription',
                                  'Difficulty', 'DisplayOrder', 'Points', 'IsActive', 'CreatedAt'],
         [[str(q['id']), str(q['quiz']), lit(q['topic']), lit(q['ar']), lit(q['type']),
           lit(image_url(q['image'][0]) if q['image'] else None), lit(q['image'][1] if q['image'] else None),
           lit(q['diff']), str(q['display']), str(q['points']), lit(q['active']), ago(51 * DAY)] for q in questions], identity=True))
qt = []
for q in questions:
    qt.append([str(q['id']), "N'ar'", lit(q['ar'])])
    if not q['en_missing']:
        qt.append([str(q['id']), "N'en'", lit(q['en'])])
w(insert('Assessment.QuestionTranslations', ['QuestionId', 'LanguageCode', 'QuestionText'], qt))
w(insert('Assessment.QuestionOptions', ['Id', 'QuestionId', 'OptionText', 'ImageUrl', 'ImageDescription', 'IsCorrect', 'DisplayOrder', 'CreatedAt'],
         [[str(o['id']), str(o['question']), lit(o['ar']), lit(image_url(o['image'][0]) if o['image'] else None),
           lit(o['image'][1] if o['image'] else None), lit(o['correct']), str(o['display']), ago(51 * DAY)]
          for q in questions for o in q['options']], identity=True))
ot = []
for q in questions:
    for o in q['options']:
        ot.append([str(o['id']), "N'ar'", lit(o['ar'])])
        if not q['en_missing']:
            ot.append([str(o['id']), "N'en'", lit(o['en'])])
w(insert('Assessment.QuestionOptionTranslations', ['QuestionOptionId', 'LanguageCode', 'OptionText'], ot))

# ---- History
w('/* ============================== Assessment: quiz history ============================== */')
w(insert('Assessment.QuizAttempts', ['Id', 'QuizId', 'UserId', 'QuestionsAnsweredCount', 'TotalQuestionsAtAttempt', 'CorrectAnswersCount',
                                     'ScorePercentage', 'Status', 'StartedAt', 'CompletedAt', 'PreviousAttemptId'],
         [[str(r['id']), str(r['quiz']), "'" + r['user'] + "'", str(r['total']), str(r['total']), str(r['correct']),
           str(r['score']), "N'Completed'", ago(r['started']), ago(r['completed']), lit(r['previous'])]
          for r in sorted(attempt_rows, key=lambda r: r['id'])], identity=True))
w(insert('Assessment.QuizAttemptQuestions', ['QuizAttemptId', 'QuestionId', 'TopicId', 'Difficulty', 'CorrectOptionId', 'QuestionType', 'Points', 'CreatedAt'],
         [[str(x['attempt']), str(x['question']), lit(x['topic']), lit(x['diff']), lit(x['correct']), lit(x['type']), str(x['points']), ago(x['created'])]
          for x in aq_rows]))
w(insert('Assessment.QuizAttemptMistakes', ['Id', 'QuizAttemptId', 'QuestionId', 'SelectedOptionId', 'CreatedAt'],
         [[str(m['id']), str(m['attempt']), str(m['question']), str(m['selected']), ago(m['created'])] for m in mistake_rows], identity=True))
w(insert('Assessment.QuizAttemptEssayAnswers', ['QuizAttemptId', 'QuestionId', 'AnswerText', 'Status', 'AwardedPoints', 'Feedback', 'GradedBy', 'GradedAt',
                                                'CreatedAt', 'LanguageCode', 'MaxPoints', 'AiOutcome', 'AiEvaluationAttempts', 'AiLastAttemptAt', 'AiConfidence'],
         [[str(e['attempt']), str(e['question']), lit(e['text']), lit(e['status']), lit(e['awarded']), lit(e['feedback']), lit(e['graded_by']),
           ago(e['graded_at']) if e['graded_at'] is not None else 'NULL', ago(e['created']), "N'ar'", str(e['max']), lit(e['outcome']),
           str(e['ai_attempts']), ago(e['ai_last']) if e['ai_last'] is not None else 'NULL', e['confidence'] or 'NULL'] for e in essay_rows]))
w(insert('Assessment.QuestionHints', ['QuizAttemptId', 'QuestionId', 'QuizAttemptMistakeId', 'HintText', 'HintSequence', 'AttemptNumber', 'LanguageCode', 'GeneratedAt'],
         [[str(h['attempt']), str(h['question']), lit(h['mistake']), lit(h['text']), str(h['seq']), lit(h['attempt_number']), "N'ar'", ago(h['generated'])]
          for h in hint_rows]))
w(insert('Assessment.UserPlacements', ['UserId', 'QuizAttemptId', 'PlacedLevelId', 'ScorePercentage', 'PassPercentage', 'PlacedAt'],
         [["'" + p['user'] + "'", str(p['attempt']), str(p['level']), str(p['score']), str(PASS_PERCENTAGE), ago(p['placed'])] for p in placement_rows]))
w(insert('Assessment.UserTopicStats', ['UserId', 'TopicId', 'Difficulty', 'QuestionsAnsweredCount', 'CorrectCount', 'HintsUsedCount',
                                       'LastQuizAttemptId', 'LastPracticedAt', 'UpdatedAt'],
         [["'" + k[0] + "'", str(k[1]), lit(k[2]), str(s['answered']), str(s['correct']), str(s['hints']), str(s['last']),
           ago(s['practiced']), ago(s['practiced'])] for k, s in sorted(stats.items())]))

w(f"""COMMIT TRANSACTION;

PRINT N'seed: development data created. Admin login: admin@volt.dev / {ADMIN_PASSWORD}';

/* What was written. */
SELECT N'Users.Users' AS TableName, COUNT(*) AS SeedRows FROM Users.Users WHERE Id LIKE N'5EED0000-%'
UNION ALL SELECT N'Users.ParentChildLinks', COUNT(*) FROM Users.ParentChildLinks WHERE ParentUserId LIKE N'5EED0000-%'
UNION ALL SELECT N'LearningContent.Levels', COUNT(*) FROM LearningContent.Levels WHERE Id BETWEEN 101 AND 199
UNION ALL SELECT N'LearningContent.Lessons', COUNT(*) FROM LearningContent.Lessons WHERE Id BETWEEN 1001 AND 1099
UNION ALL SELECT N'LearningContent.LessonContents', COUNT(*) FROM LearningContent.LessonContents WHERE Id BETWEEN 10001 AND 10999
UNION ALL SELECT N'Assessment.Topics', COUNT(*) FROM Assessment.Topics WHERE Id BETWEEN 101 AND 199
UNION ALL SELECT N'Assessment.Quizzes', COUNT(*) FROM Assessment.Quizzes WHERE Id BETWEEN 101 AND 199
UNION ALL SELECT N'Assessment.Questions', COUNT(*) FROM Assessment.Questions WHERE Id BETWEEN 1001 AND 1099
UNION ALL SELECT N'Assessment.QuestionOptions', COUNT(*) FROM Assessment.QuestionOptions WHERE Id BETWEEN 10001 AND 10999
UNION ALL SELECT N'Assessment.QuizAttempts', COUNT(*) FROM Assessment.QuizAttempts WHERE Id BETWEEN 100001 AND 100999
UNION ALL SELECT N'Assessment.UserTopicStats', COUNT(*) FROM Assessment.UserTopicStats WHERE UserId LIKE N'5EED0000-%';
GO
""")

# ---------------------------------------------------------------------------
# Removal script
# ---------------------------------------------------------------------------
users_in = ', '.join("'" + u[1] + "'" for u in USERS)
remove = f"""/* ============================================================================
   remove_seed_data.sql — deletes everything seed_dev_data.sql added
   ----------------------------------------------------------------------------
   GENERATED FILE. Development databases only.

   Deletes, by the seed's reserved ids and user ids:
     * the seed users (their refresh tokens and reset codes go with them) and links,
     * the seed levels, lessons and content blocks (content types are kept),
     * the seed categories, topics, quizzes, questions, options and translations,
     * EVERY quiz attempt on a seed quiz or by a seed user — including attempts
       real accounts made on seed quizzes — with their answers, hints,
       placements, and the topic statistics of seed topics and seed users.
   Real users placed at a seed level keep their placement row (PlacedLevelId has
   no foreign key); the removal lists them first so you can decide.

   Run: sqlcmd -S <server> -d VoltDB -b -i db/seeds/remove_seed_data.sql
   ========================================================================== */

USE VoltDB;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET NOCOUNT ON;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @SeedUsers TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
INSERT INTO @SeedUsers (Id) VALUES {', '.join("('" + u[1] + "')" for u in USERS)};

DECLARE @Attempts TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @Attempts (Id)
SELECT Id FROM Assessment.QuizAttempts
WHERE QuizId BETWEEN 101 AND 199
   OR UserId IN (SELECT Id FROM @SeedUsers)
   OR Id BETWEEN 100001 AND 100999;

-- Retries of those attempts (made later by anyone) point at them: include them too.
WHILE 1 = 1
BEGIN
    INSERT INTO @Attempts (Id)
    SELECT a.Id FROM Assessment.QuizAttempts AS a
    WHERE a.PreviousAttemptId IN (SELECT Id FROM @Attempts)
      AND a.Id NOT IN (SELECT Id FROM @Attempts);
    IF @@ROWCOUNT = 0 BREAK;
END

SELECT UserId, QuizAttemptId, PlacedLevelId, PlacedAt AS KeptPlacementAtASeedLevel
FROM Assessment.UserPlacements
WHERE PlacedLevelId BETWEEN 101 AND 199
  AND QuizAttemptId NOT IN (SELECT Id FROM @Attempts);

DELETE FROM Assessment.QuestionHints
WHERE QuizAttemptId IN (SELECT Id FROM @Attempts) OR QuestionId BETWEEN 1001 AND 1099;
DELETE FROM Assessment.UserPlacements WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.UserTopicStats
WHERE TopicId BETWEEN 101 AND 199 OR UserId IN (SELECT Id FROM @SeedUsers)
   OR LastQuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.QuizAttemptEssayAnswers WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.QuizAttemptMistakes WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.QuizAttemptQuestions WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);

-- Newest first, so a retry is deleted before the attempt it points at.
DECLARE @Deleted INT = 1;
WHILE @Deleted > 0
BEGIN
    DELETE FROM Assessment.QuizAttempts
    WHERE Id IN (SELECT Id FROM @Attempts)
      AND Id NOT IN (SELECT PreviousAttemptId FROM Assessment.QuizAttempts WHERE PreviousAttemptId IS NOT NULL);
    SET @Deleted = @@ROWCOUNT;
END

DELETE FROM Assessment.QuestionOptionTranslations
WHERE QuestionOptionId IN (SELECT Id FROM Assessment.QuestionOptions WHERE QuestionId BETWEEN 1001 AND 1099);
DELETE FROM Assessment.QuestionOptions WHERE QuestionId BETWEEN 1001 AND 1099;
DELETE FROM Assessment.QuestionTranslations WHERE QuestionId BETWEEN 1001 AND 1099;
DELETE FROM Assessment.Questions WHERE Id BETWEEN 1001 AND 1099 OR QuizId BETWEEN 101 AND 199;
DELETE FROM Assessment.QuizTranslations WHERE QuizId BETWEEN 101 AND 199;
DELETE FROM Assessment.Quizzes WHERE Id BETWEEN 101 AND 199;
DELETE FROM Assessment.TopicTranslations WHERE TopicId BETWEEN 101 AND 199;
DELETE FROM Assessment.Topics WHERE Id BETWEEN 101 AND 199;
DELETE FROM Assessment.CategoryTranslations WHERE CategoryId BETWEEN 101 AND 199;
DELETE FROM Assessment.Categories WHERE Id BETWEEN 101 AND 199;

DELETE FROM LearningContent.LessonContents WHERE Id BETWEEN 10001 AND 10999 OR LessonId BETWEEN 1001 AND 1099;
DELETE FROM LearningContent.Lessons WHERE Id BETWEEN 1001 AND 1099;
DELETE FROM LearningContent.Levels WHERE Id BETWEEN 101 AND 199;

DELETE FROM Users.ParentChildLinks
WHERE ParentUserId IN (SELECT Id FROM @SeedUsers) OR ChildUserId IN (SELECT Id FROM @SeedUsers);
DELETE FROM Users.Users WHERE Id IN (SELECT Id FROM @SeedUsers);

COMMIT TRANSACTION;
PRINT N'seed: development data removed.';
GO
"""

os.makedirs(SEED_DIR, exist_ok=True)
with open(f'{SEED_DIR}/seed_dev_data.sql', 'w', encoding='utf-8') as f:
    f.write('\n'.join(sql))
with open(f'{SEED_DIR}/remove_seed_data.sql', 'w', encoding='utf-8') as f:
    f.write(remove)

# ---------------------------------------------------------------------------
# Placeholder images (plain shapes, no external libraries)
# ---------------------------------------------------------------------------


class Canvas:
    def __init__(self, w, h, bg):
        self.w, self.h = w, h
        self.rows = [bytearray(bytes(bg) * w) for _ in range(h)]

    def rect(self, x0, y0, x1, y1, c):
        x0, x1 = max(0, int(x0)), min(self.w, int(x1))
        for y in range(max(0, int(y0)), min(self.h, int(y1))):
            self.rows[y][x0 * 3:x1 * 3] = bytes(c) * max(0, x1 - x0)

    def circle(self, cx, cy, r, c):
        for y in range(int(cy - r), int(cy + r) + 1):
            if 0 <= y < self.h:
                dx = int((r * r - (y - cy) ** 2) ** 0.5) if r * r >= (y - cy) ** 2 else -1
                if dx >= 0:
                    self.rect(cx - dx, y, cx + dx + 1, y + 1, c)

    def save(self, path):
        raw = b''.join(b'\x00' + bytes(r) for r in self.rows)

        def chunk(tag, data):
            return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)

        with open(path, 'wb') as f:
            f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', self.w, self.h, 8, 2, 0, 0, 0))
                    + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


YELLOW, GREY, DARK, WHITE = (255, 205, 60), (120, 130, 140), (45, 52, 64), (255, 255, 255)
GREEN, RED, COPPER, BROWN = (70, 170, 110), (225, 80, 70), (205, 120, 60), (170, 115, 70)


def bulb(cv, cx, cy, s, on=True):
    cv.circle(cx, cy, 34 * s, YELLOW if on else (200, 200, 200))
    cv.rect(cx - 16 * s, cy + 28 * s, cx + 16 * s, cy + 52 * s, GREY)
    if on:
        for dx, dy, w_, h_ in ((-4, -70, 8, 20), (-4, 50 + 20, 0, 0), (-70, -4, 20, 8), (50, -4, 20, 8)):
            if w_:
                cv.rect(cx + dx * s, cy + dy * s, cx + (dx + w_) * s, cy + (dy + h_) * s, YELLOW)


def battery(cv, cx, cy, s):
    cv.rect(cx - 70 * s, cy - 32 * s, cx + 70 * s, cy + 32 * s, GREEN)
    cv.rect(cx + 70 * s, cy - 12 * s, cx + 84 * s, cy + 12 * s, GREY)
    cv.rect(cx + 30 * s, cy - 4 * s, cx + 54 * s, cy + 4 * s, WHITE)
    cv.rect(cx + 38 * s, cy - 12 * s, cx + 46 * s, cy + 12 * s, WHITE)
    cv.rect(cx - 54 * s, cy - 4 * s, cx - 30 * s, cy + 4 * s, WHITE)


def loop(cv, x0, y0, x1, y1, t, c, gap=False):
    cv.rect(x0, y0, x1, y0 + t, c)
    cv.rect(x0, y0, x0 + t, y1, c)
    cv.rect(x1 - t, y0, x1, y1, c)
    if gap:
        mid = (x0 + x1) // 2
        cv.rect(x0, y1 - t, mid - 30, y1, c)
        cv.rect(mid + 30, y1 - t, x1, y1, c)
    else:
        cv.rect(x0, y1 - t, x1, y1, c)


def draw(key, w, h, bg):
    cv = Canvas(w, h, bg)
    cx, cy, s = w // 2, h // 2, min(w, h) / 320
    if key in ('lesson-electricity', 'opt-bulb'):
        bulb(cv, cx, cy - 10 * s, s * 1.4)
    elif key == 'q-battery':
        battery(cv, cx, cy, s * 1.3)
    elif key == 'opt-copper':
        for i in range(6):
            cv.circle(cx - 100 * s + i * 40 * s, cy, 26 * s, COPPER)
            cv.circle(cx - 100 * s + i * 40 * s, cy, 14 * s, bg)
    elif key == 'opt-rubber':
        cv.rect(cx - 90 * s, cy - 50 * s, cx + 90 * s, cy + 50 * s, DARK)
    elif key == 'opt-wood':
        cv.rect(cx - 100 * s, cy - 45 * s, cx + 100 * s, cy + 45 * s, BROWN)
        for i in range(4):
            cv.rect(cx - 100 * s, cy - 35 * s + i * 22 * s, cx + 100 * s, cy - 31 * s + i * 22 * s, (140, 90, 50))
    elif key in ('lesson-circuit', 'q-open-circuit'):
        open_ = key != 'lesson-circuit'
        loop(cv, cx - 120 * s, cy - 80 * s, cx + 120 * s, cy + 80 * s, 10 * s, DARK, gap=open_)
        cv.rect(cx - 135 * s, cy - 30 * s, cx - 105 * s, cy + 30 * s, GREEN)
        bulb(cv, cx, cy - 80 * s, s * 0.8, on=not open_)
    elif key == 'lesson-resistor':
        cv.rect(cx - 140 * s, cy - 5 * s, cx + 140 * s, cy + 5 * s, GREY)
        cv.rect(cx - 60 * s, cy - 26 * s, cx + 60 * s, cy + 26 * s, (225, 195, 150))
        for i, c in enumerate(((150, 90, 40), DARK, RED, YELLOW)):
            cv.rect(cx - 45 * s + i * 25 * s, cy - 26 * s, cx - 33 * s + i * 25 * s, cy + 26 * s, c)
    elif key == 'lesson-led':
        cv.circle(cx, cy - 40 * s, 40 * s, RED)
        cv.rect(cx - 40 * s, cy - 40 * s, cx + 40 * s, cy + 10 * s, RED)
        cv.rect(cx - 25 * s, cy + 10 * s, cx - 17 * s, cy + 110 * s, GREY)
        cv.rect(cx + 17 * s, cy + 10 * s, cx + 25 * s, cy + 80 * s, GREY)
    elif key == 'lesson-capacitor':
        cv.rect(cx - 140 * s, cy - 5 * s, cx - 20 * s, cy + 5 * s, GREY)
        cv.rect(cx + 20 * s, cy - 5 * s, cx + 140 * s, cy + 5 * s, GREY)
        cv.rect(cx - 20 * s, cy - 60 * s, cx - 8 * s, cy + 60 * s, DARK)
        cv.rect(cx + 8 * s, cy - 60 * s, cx + 20 * s, cy + 60 * s, DARK)
    elif key == 'lesson-safety':
        # A socket inside a red "no" sign.
        cv.circle(cx, cy, 120 * s, RED)
        cv.circle(cx, cy, 104 * s, bg)
        cv.circle(cx, cy, 78 * s, WHITE)
        cv.rect(cx - 36 * s, cy - 22 * s, cx - 22 * s, cy + 22 * s, DARK)
        cv.rect(cx + 22 * s, cy - 22 * s, cx + 36 * s, cy + 22 * s, DARK)
        for i in range(-80, 81, 4):
            cv.rect(cx + (i - 9) * s, cy + (i - 9) * s, cx + (i + 9) * s, cy + (i + 9) * s, RED)
    elif key == 'lesson-button':
        # A red push button on its base, with its two legs.
        cv.rect(cx - 90 * s, cy + 10 * s, cx + 90 * s, cy + 70 * s, GREY)
        cv.rect(cx - 55 * s, cy - 8 * s, cx + 55 * s, cy + 10 * s, DARK)
        cv.circle(cx, cy - 30 * s, 50 * s, RED)
        cv.rect(cx - 70 * s, cy + 70 * s, cx - 58 * s, cy + 130 * s, GREY)
        cv.rect(cx + 58 * s, cy + 70 * s, cx + 70 * s, cy + 130 * s, GREY)
    else:
        raise KeyError(key)
    return cv


os.makedirs(IMG_DIR, exist_ok=True)
image_keys = {l[6] for l in LESSONS}
image_keys |= {q['image'][0] for q in questions if q['image']}
image_keys |= {o['image'][0] for o in options_by_id.values() if o['image']}
backgrounds = [(232, 244, 253), (237, 247, 237), (253, 243, 231), (243, 237, 250)]
for i, key in enumerate(sorted(image_keys)):
    w_, h_ = (640, 360) if key.startswith('lesson-') else (320, 320)
    draw(key, w_, h_, backgrounds[i % len(backgrounds)]).save(f'{IMG_DIR}/seed-{key}.png')

print(f'questions={len(questions)} options={len(options_by_id)} attempts={len(attempt_rows)} '
      f'aq={len(aq_rows)} mistakes={len(mistake_rows)} essays={len(essay_rows)} hints={len(hint_rows)} '
      f'placements={[(p["attempt"], p["level"], str(p["score"])) for p in placement_rows]} stats={len(stats)} images={len(image_keys)}')
for key, user_id, *_ in USERS:
    if xp[user_id] or stats_of_user(user_id):
        print(f'{key}: xp={xp[user_id]} mastered topics={mastered_topics(user_id)} stat rows={stats_of_user(user_id)}')
for r in sorted(attempt_rows, key=lambda r: r['id']):
    print(r['id'], r['quiz'], r['total'], r['correct'], r['score'])
