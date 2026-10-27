//! The Windows desktop visual language, drawn with AppKit (no browser or assets).
//! Coordinates are the same 380 logical units used by desktop_ui.rs.
use crate::ui_model::{Action, Tone};
use objc2::rc::{Allocated, Retained};
use objc2::runtime::{AnyObject, Bool, ClassBuilder, Sel};
use objc2::{class, msg_send, sel};
use objc2_foundation::{NSPoint, NSRect, NSSize, NSString};
use std::cell::RefCell;

pub const WIDTH: f64 = 380.;
pub const BG: u32 = 0xf6f7f9;
pub const WHITE: u32 = 0xffffff;
pub const TEXT: u32 = 0x182230;
pub const MUTED: u32 = 0x667085;
pub const BLUE: u32 = 0x2563eb;
pub const LINE: u32 = 0xe4e7ec;
pub const SOFT: u32 = 0xebeff3;
type Object = Retained<AnyObject>;

// Callers use the desktop convention: 0 = left, 1 = right, 2 = center.
// AppKit swaps the numeric values for center/right between ARM and Intel.
fn native_alignment(alignment: isize) -> objc2_app_kit::NSTextAlignment {
    use objc2_app_kit::NSTextAlignment;
    match alignment {
        1 => NSTextAlignment::Right,
        2 => NSTextAlignment::Center,
        _ => NSTextAlignment::Left,
    }
}

#[derive(Clone, Copy, PartialEq)]
pub struct Canvas {
    pub page: u8,
    pub home_extra: f64,
    pub info_extra: f64,
    pub tone: Tone,
    pub info_tone: Tone,
    pub action: Action,
    pub busy: bool,
    pub spin: f64,
    pub focus_field: isize,
    pub invalid_field: isize,
}
impl Default for Canvas {
    fn default() -> Self {
        Self {
            page: 0,
            home_extra: 0.,
            info_extra: 0.,
            tone: Tone::Neutral,
            info_tone: Tone::Neutral,
            action: Action::Account,
            busy: false,
            spin: 0.,
            focus_field: 0,
            invalid_field: 0,
        }
    }
}
thread_local! { static CANVAS: RefCell<Canvas> = RefCell::new(Canvas::default()); }
pub fn update(canvas: Canvas) -> bool {
    CANVAS.with(|cell| {
        let changed = *cell.borrow() != canvas;
        *cell.borrow_mut() = canvas;
        changed
    })
}
fn canvas() -> Canvas {
    CANVAS.with(|cell| *cell.borrow())
}
pub fn rect(x: f64, y: f64, w: f64, h: f64) -> NSRect {
    NSRect::new(NSPoint::new(x, y), NSSize::new(w, h))
}
pub unsafe fn color(rgb: u32) -> Object {
    msg_send![class!(NSColor), colorWithSRGBRed: ((rgb>>16)&255) as f64/255.,
        green: ((rgb>>8)&255) as f64/255., blue: (rgb&255) as f64/255., alpha:1.0f64]
}
pub unsafe fn font(size: f64, bold: bool) -> Object {
    msg_send![class!(NSFont), systemFontOfSize:size, weight:if bold { 0.3f64 } else { 0.0f64 }]
}
fn tone_color(tone: Tone) -> u32 {
    match tone {
        Tone::Good => 0x167a4a,
        Tone::Warning => 0x995c18,
        Tone::Error => 0xbd353b,
        Tone::Neutral => BLUE,
    }
}
unsafe fn fill(rgb: u32) {
    let _: () = msg_send![&*color(rgb), setFill];
}
unsafe fn stroke(rgb: u32) {
    let _: () = msg_send![&*color(rgb), setStroke];
}
unsafe fn rounded(r: NSRect, radius: f64, bg: u32, border: Option<u32>) {
    let r = rect(
        r.origin.x + 0.5,
        r.origin.y + 0.5,
        r.size.width - 1.,
        r.size.height - 1.,
    );
    let path: Object =
        msg_send![class!(NSBezierPath),bezierPathWithRoundedRect:r,xRadius:radius,yRadius:radius];
    fill(bg);
    let _: () = msg_send![&path, fill];
    if let Some(rgb) = border {
        stroke(rgb);
        let _: () = msg_send![&path,setLineWidth:1.0f64];
        let _: () = msg_send![&path, stroke];
    }
}
unsafe fn ellipse(r: NSRect, bg: u32, border: Option<u32>) {
    let path: Object = msg_send![class!(NSBezierPath),bezierPathWithOvalInRect:r];
    fill(bg);
    let _: () = msg_send![&path, fill];
    if let Some(rgb) = border {
        stroke(rgb);
        let _: () = msg_send![&path,setLineWidth:1.0f64];
        let _: () = msg_send![&path, stroke];
    }
}
unsafe fn line(x: f64, y: f64, x2: f64, y2: f64, rgb: u32) {
    let path: Object = msg_send![class!(NSBezierPath), bezierPath];
    let _: () = msg_send![&path,moveToPoint:NSPoint::new(x,y)];
    let _: () = msg_send![&path,lineToPoint:NSPoint::new(x2,y2)];
    stroke(rgb);
    let _: () = msg_send![&path,setLineWidth:1.0f64];
    let _: () = msg_send![&path, stroke];
}
#[link(name = "AppKit", kind = "framework")]
extern "C" {
    static NSFontAttributeName: *const AnyObject;
    static NSForegroundColorAttributeName: *const AnyObject;
    static NSParagraphStyleAttributeName: *const AnyObject;
}
unsafe fn attributes(size: f64, bold: bool, rgb: u32, alignment: isize) -> Object {
    let font = font(size, bold);
    let color = color(rgb);
    let paragraph: Object = msg_send![class!(NSMutableParagraphStyle), new];
    let _: () = msg_send![&paragraph,setAlignment:native_alignment(alignment)];
    let _: () = msg_send![&paragraph,setLineBreakMode:0usize];
    let objects = [&*font as *const AnyObject, &*color, &*paragraph];
    let keys = [
        NSFontAttributeName,
        NSForegroundColorAttributeName,
        NSParagraphStyleAttributeName,
    ];
    msg_send![class!(NSDictionary),dictionaryWithObjects:objects.as_ptr(),forKeys:keys.as_ptr(),count:3usize]
}
unsafe fn text(value: &str, r: NSRect, size: f64, bold: bool, rgb: u32, alignment: isize) {
    let attrs = attributes(size, bold, rgb, alignment);
    let _: () = msg_send![&*NSString::from_str(value),drawInRect:r,withAttributes:&*attrs];
}
pub unsafe fn text_height(value: &str, width: f64, size: f64) -> f64 {
    let attrs = attributes(size, false, TEXT, 0);
    let measured: NSRect = msg_send![&*NSString::from_str(value),boundingRectWithSize:NSSize::new(width,10000.),options:3usize,attributes:&*attrs];
    measured.size.height.ceil()
}
pub unsafe fn configure_label(
    field: &AnyObject,
    size: f64,
    bold: bool,
    rgb: u32,
    alignment: isize,
) {
    let _: () = msg_send![field,setFont:&*font(size,bold)];
    let _: () = msg_send![field,setTextColor:&*color(rgb)];
    let _: () = msg_send![field,setAlignment:native_alignment(alignment)];
    let cell: *mut AnyObject = msg_send![field, cell];
    let _: () = msg_send![cell,setWraps:true];
    let _: () = msg_send![cell,setUsesSingleLineMode:false];
}

pub unsafe fn register_classes() {
    let mut view = ClassBuilder::new(c"SNNUContentView", class!(NSView)).unwrap();
    view.add_method(
        sel!(drawRect:),
        draw_background as unsafe extern "C-unwind" fn(_, _, _),
    );
    view.add_method(sel!(isOpaque), yes as extern "C-unwind" fn(_, _) -> _);
    view.add_method(sel!(isFlipped), yes as extern "C-unwind" fn(_, _) -> _);
    view.add_method(
        sel!(wantsUpdateLayer),
        no as extern "C-unwind" fn(_, _) -> _,
    );
    view.register();
    let mut button = ClassBuilder::new(c"SNNUDesktopButton", class!(NSButton)).unwrap();
    button.add_method(
        sel!(drawRect:),
        draw_button as unsafe extern "C-unwind" fn(_, _, _),
    );
    button.add_method(sel!(isFlipped), yes as extern "C-unwind" fn(_, _) -> _);
    button.add_method(
        sel!(wantsUpdateLayer),
        no as extern "C-unwind" fn(_, _) -> _,
    );
    button.add_method(
        sel!(acceptsFirstResponder),
        yes as extern "C-unwind" fn(_, _) -> _,
    );
    button.add_method(
        sel!(mouseEntered:),
        hover_changed as unsafe extern "C-unwind" fn(_, _, _),
    );
    button.add_method(
        sel!(mouseExited:),
        hover_changed as unsafe extern "C-unwind" fn(_, _, _),
    );
    button.add_method(
        sel!(hitTest:),
        hit_test as unsafe extern "C-unwind" fn(_, _, _) -> _,
    );
    button.register();
}
pub unsafe fn track(button: &AnyObject) {
    let area: Allocated<AnyObject> = msg_send![class!(NSTrackingArea), alloc];
    let area: Object = msg_send![area,initWithRect:rect(0.,0.,0.,0.),options:0x221usize,owner:button,userInfo:std::ptr::null::<AnyObject>()];
    let _: () = msg_send![button,addTrackingArea:&*area];
}
extern "C-unwind" fn yes(_: &AnyObject, _: Sel) -> Bool {
    Bool::YES
}
extern "C-unwind" fn no(_: &AnyObject, _: Sel) -> Bool {
    Bool::NO
}
unsafe extern "C-unwind" fn hover_changed(this: &AnyObject, _: Sel, _: &AnyObject) {
    let _: () = msg_send![this,setNeedsDisplay:true];
}
unsafe extern "C-unwind" fn hit_test(this: &AnyObject, _: Sel, p: NSPoint) -> *mut AnyObject {
    let tag: isize = msg_send![this, tag];
    if tag == 10 {
        let parent: *mut AnyObject = msg_send![this, superview];
        let p: NSPoint = msg_send![this,convertPoint:p,fromView:parent];
        if (p.x - 68.).powi(2) + (p.y - 68.).powi(2) > 68. * 68. {
            return std::ptr::null_mut();
        }
    }
    msg_send![super(this,class!(NSButton)),hitTest:p]
}
unsafe extern "C-unwind" fn draw_background(this: &AnyObject, _: Sel, dirty: NSRect) {
    fill(BG);
    objc2_app_kit::NSRectFill(dirty);
    let c = canvas();
    match c.page {
        0 => rounded(
            rect(24., 330. + c.home_extra, 332., 74.),
            6.,
            WHITE,
            Some(LINE),
        ),
        2 => {
            for (top, id) in [(140., 201), (226., 202)] {
                rounded(
                    rect(24., top, 332., 46.),
                    5.,
                    WHITE,
                    Some(if c.invalid_field == id {
                        0xbd353b
                    } else if c.focus_field == id {
                        BLUE
                    } else {
                        LINE
                    }),
                );
            }
            rounded(rect(24., 316., 332., 42.), 5., SOFT, None);
        }
        3 => {
            rounded(
                rect(24., 72., 332., 104. + c.info_extra),
                6.,
                WHITE,
                Some(LINE),
            );
            ellipse(rect(40., 96., 12., 12.), tone_color(c.info_tone), None);
            rounded(
                rect(24., 192. + c.info_extra, 332., 216.),
                6.,
                WHITE,
                Some(LINE),
            );
            for i in 1..5 {
                let y = 192. + c.info_extra + i as f64 * 40.;
                line(40., y, 340., y, LINE);
            }
        }
        4 => {
            for group in 0..3 {
                let y = 100. + group as f64 * 212.;
                rounded(rect(24., y, 332., 176.), 6., WHITE, Some(LINE));
                for row in 1..3 {
                    line(
                        40.,
                        y + 4. + row as f64 * 56.,
                        340.,
                        y + 4. + row as f64 * 56.,
                        LINE,
                    );
                }
            }
        }
        5 => rounded(rect(24., 72., 332., 440.), 6., WHITE, Some(LINE)),
        _ => {}
    }
    let _: NSRect = msg_send![this, bounds];
}
unsafe extern "C-unwind" fn draw_button(this: &AnyObject, _: Sel, _: NSRect) {
    let bounds: NSRect = msg_send![this, bounds];
    let w = bounds.size.width;
    let h = bounds.size.height;
    let tag: isize = msg_send![this, tag];
    let enabled: bool = msg_send![this, isEnabled];
    let cell: *mut AnyObject = msg_send![this, cell];
    let pressed: bool = msg_send![cell, isHighlighted];
    let state: isize = msg_send![this, state];
    let title: Retained<NSString> = msg_send![this, title];
    let title = title.to_string();
    let window: *mut AnyObject = msg_send![this, window];
    let cursor: NSPoint = msg_send![window, mouseLocationOutsideOfEventStream];
    let cursor: NSPoint =
        msg_send![this,convertPoint:cursor,fromView:std::ptr::null::<AnyObject>()];
    let hover = cursor.x >= 0. && cursor.y >= 0. && cursor.x < w && cursor.y < h;
    let c = canvas();
    let mut fg = if enabled { TEXT } else { 0xa0aab5 };
    fill(if (30..=32).contains(&tag) {
        SOFT
    } else if tag == 17 {
        WHITE
    } else {
        BG
    });
    objc2_app_kit::NSRectFill(bounds);
    match tag {
        10 => {
            let fg = if c.busy { BLUE } else { tone_color(c.tone) };
            let bg = if c.busy {
                SOFT
            } else {
                match c.tone {
                    Tone::Good => {
                        if hover || pressed {
                            0xd9ecd5
                        } else {
                            0xe8f4e5
                        }
                    }
                    Tone::Warning => {
                        if hover || pressed {
                            0xfae6ce
                        } else {
                            0xfcf1df
                        }
                    }
                    Tone::Error => {
                        if hover || pressed {
                            0xfadcd9
                        } else {
                            0xfdebeb
                        }
                    }
                    Tone::Neutral => {
                        if hover || pressed {
                            0xdce6f6
                        } else {
                            0xe8f0fc
                        }
                    }
                }
            };
            ellipse(rect(2., 2., 132., 132.), bg, None);
            if !c.busy && c.action == Action::Account {
                text(
                    if c.tone == Tone::Neutral { "+" } else { "!" },
                    rect(9., 26., 118., 51.),
                    29.333,
                    true,
                    fg,
                    2,
                );
            } else {
                let path: Object = msg_send![class!(NSBezierPath), bezierPath];
                // 270 degree power ring, opening above; same 20-unit radius as Windows.
                let (start, end) = if c.busy {
                    (c.spin, c.spin + 90.)
                } else {
                    (310., 590.)
                };
                let _: () = msg_send![&path,appendBezierPathWithArcWithCenter:NSPoint::new(68.,52.),radius:20.0f64,startAngle:start,endAngle:end,clockwise:false];
                stroke(fg);
                let _: () = msg_send![&path,setLineWidth:2.0f64];
                let _: () = msg_send![&path,setLineCapStyle:1usize];
                let _: () = msg_send![&path, stroke];
                if !c.busy {
                    let p: Object = msg_send![class!(NSBezierPath), bezierPath];
                    let _: () = msg_send![&p,moveToPoint:NSPoint::new(68.,27.)];
                    let _: () = msg_send![&p,lineToPoint:NSPoint::new(68.,50.)];
                    let _: () = msg_send![&p,setLineWidth:2.0f64];
                    let _: () = msg_send![&p,setLineCapStyle:1usize];
                    let _: () = msg_send![&p, stroke];
                }
            }
            text(&title, rect(9., 93., 118., 26.), 13.333, true, fg, 2);
        }
        2 | 3 | 4 | 11 | 12 => {
            rounded(
                bounds,
                5.,
                if hover && enabled { 0xf9fafc } else { WHITE },
                Some(LINE),
            );
            let mut lines = title.splitn(2, '\n');
            let first = lines.next().unwrap_or("");
            let second = lines.next();
            text(
                first,
                rect(
                    16.,
                    if second.is_some() {
                        10.
                    } else {
                        (h - 22.) / 2.
                    },
                    w - 84.,
                    24.,
                ),
                13.333,
                true,
                fg,
                0,
            );
            if let Some(second) = second {
                text(
                    second,
                    rect(16., 36., w - 84., h - 42.),
                    12.,
                    false,
                    if enabled { MUTED } else { fg },
                    0,
                );
            }
            if tag == 11 || tag == 12 {
                let x = w - 54.;
                let y = h / 2. - 11.;
                rounded(
                    rect(x, y, 38., 22.),
                    11.,
                    if state == 1 { BLUE } else { 0xb8c0c9 },
                    None,
                );
                ellipse(
                    rect(x + if state == 1 { 19. } else { 3. }, y + 3., 16., 16.),
                    WHITE,
                    None,
                );
            } else {
                text(
                    "›",
                    rect(w - 38., (h - 24.) / 2., 26., 24.),
                    17.333,
                    false,
                    MUTED,
                    2,
                );
            }
        }
        13 | 15 => {
            let bg = if !enabled {
                0xbdcbd7
            } else if pressed {
                0x1d4fc6
            } else if hover {
                0x2058db
            } else {
                BLUE
            };
            rounded(bounds, 5., bg, None);
            text(
                &title,
                rect(0., (h - 22.) / 2., w, 24.),
                13.333,
                true,
                WHITE,
                2,
            );
        }
        30..=32 => {
            if state == 1 {
                rounded(bounds, 5., WHITE, Some(LINE));
            }
            text(
                &title,
                rect(0., (h - 22.) / 2., w, 24.),
                13.333,
                true,
                if state == 1 { BLUE } else { MUTED },
                2,
            );
        }
        _ => {
            if hover && enabled && tag != 17 {
                rounded(bounds, 5., SOFT, None);
            }
            if enabled {
                fg = if tag == 14 {
                    MUTED
                } else if tag == 17 {
                    TEXT
                } else {
                    BLUE
                };
            }
            text(
                &title,
                rect(0., (h - 22.) / 2., w, 24.),
                if tag == 17 { 13.333 } else { 13.333 },
                tag != 17,
                fg,
                if tag == 17 { 1 } else { 2 },
            );
        }
    }
    let focused: *mut AnyObject = msg_send![window, firstResponder];
    if focused == this as *const _ as *mut _ {
        let path: Object = if tag == 10 {
            msg_send![class!(NSBezierPath),bezierPathWithOvalInRect:rect(3.,3.,w-6.,h-6.)]
        } else {
            msg_send![class!(NSBezierPath),bezierPathWithRoundedRect:rect(3.,3.,w-6.,h-6.),xRadius:3.0f64,yRadius:3.0f64]
        };
        stroke(BLUE);
        let dash = [2.0f64, 2.];
        let _: () = msg_send![&path,setLineDash:dash.as_ptr(),count:2isize,phase:0.0f64];
        let _: () = msg_send![&path, stroke];
    }
}
