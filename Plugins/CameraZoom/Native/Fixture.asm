.const
one REAL4 1.0
.code
PUBLIC CameraFixture
CameraFixture PROC FRAME
 push rsi
 .pushreg rsi
 .endprolog
 mov rsi, rcx
 xorps xmm0, xmm0
 maxss xmm1, xmm0
 minss xmm1, DWORD PTR [one]
 movss DWORD PTR [rsi+528h], xmm1
 movaps xmm0, xmm1
 pop rsi
 ret
CameraFixture ENDP
END
