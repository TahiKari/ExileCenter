; Preserve integer/pointer arguments, including stack arguments, then tail-call
; Windows. Version APIs do not have floating-point or vector arguments.
EXTERN ResolveVersion:PROC
.code
Forward MACRO n
PUBLIC Proxy&n
Proxy&n PROC FRAME
 push rcx
 .allocstack 8
 push rdx
 .allocstack 8
 push r8
 .allocstack 8
 push r9
 .allocstack 8
 sub rsp, 40
 .allocstack 40
 .endprolog
 mov ecx, n
 call ResolveVersion
 add rsp, 40
 pop r9
 pop r8
 pop rdx
 pop rcx
 jmp rax
Proxy&n ENDP
ENDM
Forward 0
Forward 1
Forward 2
Forward 3
Forward 4
Forward 5
Forward 6
Forward 7
Forward 8
Forward 9
Forward 10
Forward 11
Forward 12
Forward 13
Forward 14
Forward 15
Forward 16
END
