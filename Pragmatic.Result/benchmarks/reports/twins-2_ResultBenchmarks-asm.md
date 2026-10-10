## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.CreateSuccess()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+30]
       mov       r8,[rcx+8]
       cmp       eax,[r8+8]
       jae       short M00_L01
       mov       r10d,eax
       mov       r8d,[r8+r10*4+10]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       shl       r10,4
       lea       rdx,[rdx+r10+10]
       xor       r10d,r10d
       mov       [rdx],r10
       mov       [rdx+8],r8d
       mov       byte ptr [rdx+0C],1
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 76
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.CreateFailure()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       xor       esi,esi
       mov       rdi,[rbx+30]
       mov       rcx,176C84008A0
       mov       rbp,[rcx]
       mov       rdx,rbp
       jmp       short M00_L01
M00_L00:
       mov       rdi,[rbx+30]
       mov       rdx,rbp
M00_L01:
       cmp       esi,[rdi+8]
       jae       short M00_L02
       mov       rcx,rsi
       shl       rcx,4
       lea       rdi,[rdi+rcx+10]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       xor       eax,eax
       mov       [rdi+8],eax
       mov       byte ptr [rdi+0C],0
       inc       esi
       cmp       esi,400
       jl        short M00_L00
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L02:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 101
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.CreateSuccess_Implicit()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+30]
       mov       r8,[rcx+8]
       cmp       eax,[r8+8]
       jae       short M00_L01
       mov       r10d,eax
       mov       r8d,[r8+r10*4+10]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       shl       r10,4
       lea       rdx,[rdx+r10+10]
       xor       r10d,r10d
       mov       [rdx],r10
       mov       [rdx+8],r8d
       mov       byte ptr [rdx+0C],1
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 76
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.CreateFailure_Implicit()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       xor       esi,esi
       mov       rdi,[rbx+30]
       mov       rcx,26FAC4008A0
       mov       rbp,[rcx]
       mov       rdx,rbp
       jmp       short M00_L01
M00_L00:
       mov       rdi,[rbx+30]
       mov       rdx,rbp
M00_L01:
       cmp       esi,[rdi+8]
       jae       short M00_L02
       mov       rcx,rsi
       shl       rcx,4
       lea       rdi,[rdi+rcx+10]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       xor       eax,eax
       mov       [rdi+8],eax
       mov       byte ptr [rdi+0C],0
       inc       esi
       cmp       esi,400
       jl        short M00_L00
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L02:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 101
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.IsSuccess_Check()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+10]
       xor       edx,edx
       jmp       short M00_L02
M00_L00:
       inc       eax
M00_L01:
       inc       edx
       cmp       edx,400
       jge       short M00_L03
M00_L02:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L04
       mov       r10,rdx
       shl       r10,4
       cmp       byte ptr [r8+r10+1C],0
       je        short M00_L01
       jmp       short M00_L00
M00_L03:
       add       rsp,28
       ret
M00_L04:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 63
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Value_DirectAccess()
       sub       rsp,28
       xor       eax,eax
       xor       edx,edx
       mov       rcx,[rcx+18]
       mov       r8,rcx
       mov       r10d,[r8+8]
       test      r10d,r10d
       je        short M00_L03
       add       r8,10
       cmp       byte ptr [r8+0C],0
       je        short M00_L02
M00_L00:
       add       eax,[r8+8]
       inc       edx
       cmp       edx,400
       jge       short M00_L01
       mov       r8,rcx
       cmp       edx,r10d
       jae       short M00_L03
       mov       r9,rdx
       shl       r9,4
       lea       r8,[r8+r9+10]
       cmp       byte ptr [r8+0C],0
       jne       short M00_L00
       jmp       short M00_L02
M00_L01:
       add       rsp,28
       ret
M00_L02:
       mov       rcx,offset MT_Pragmatic.Result.Result`2[[System.Int32, System.Private.CoreLib],[Pragmatic.Result.Benchmarks.BenchmarkError, Pragmatic.Result.Benchmarks]]
       call      qword ptr [7FF8145B6A30]
       int       3
M00_L03:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 106
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.TryGetValue_Pattern()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+10]
       xor       edx,edx
       jmp       short M00_L01
M00_L00:
       add       eax,r10d
       inc       edx
       cmp       edx,400
       jge       short M00_L02
M00_L01:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L03
       mov       r10,rdx
       shl       r10,4
       lea       r8,[r8+r10+10]
       mov       r10d,[r8+8]
       cmp       byte ptr [r8+0C],0
       jne       short M00_L00
       xor       r10d,r10d
       jmp       short M00_L00
M00_L02:
       add       rsp,28
       ret
M00_L03:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 75
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Match_Pattern()
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       xor       esi,esi
       xor       edi,edi
       jmp       short M00_L02
M00_L00:
       mov       edx,[rbp+8]
       mov       rcx,7FF8145C2B08
       cmp       [r14+18],rcx
       jne       near ptr M00_L10
M00_L01:
       add       esi,edx
       inc       edi
       cmp       edi,400
       jge       short M00_L06
M00_L02:
       mov       rcx,[rbx+10]
       cmp       edi,[rcx+8]
       jae       near ptr M00_L11
       mov       rax,rdi
       shl       rax,4
       lea       rbp,[rcx+rax+10]
       mov       rcx,1E8D00008C0
       mov       r14,[rcx]
       test      r14,r14
       je        short M00_L07
M00_L03:
       mov       rcx,1E8D00008C8
       mov       rax,[rcx]
       test      rax,rax
       je        near ptr M00_L08
M00_L04:
       cmp       byte ptr [rbp+0C],0
       jne       short M00_L00
       mov       rdx,[rbp]
       mov       rcx,7FF8145C2B20
       cmp       [rax+18],rcx
       jne       near ptr M00_L09
       xor       edx,edx
M00_L05:
       jmp       short M00_L01
M00_L06:
       mov       eax,esi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M00_L07:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,1E8D00008B8
       mov       rdx,[rcx]
       lea       rcx,[r14+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145C2B08
       mov       [r14+18],rcx
       mov       rcx,1E8D00008C0
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L03
M00_L08:
       mov       rcx,offset MT_System.Func`2[[Pragmatic.Result.Benchmarks.BenchmarkError, Pragmatic.Result.Benchmarks],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,1E8D00008B8
       mov       rdx,[rcx]
       lea       rcx,[r15+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145C2B20
       mov       [r15+18],rcx
       mov       rcx,1E8D00008C8
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r15
       jmp       near ptr M00_L04
M00_L09:
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       edx,eax
       jmp       near ptr M00_L05
M00_L10:
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       mov       edx,eax
       jmp       near ptr M00_L01
M00_L11:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 362
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Map_SingleTransform()
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,30
       xor       eax,eax
       mov       [rsp+28],rax
       mov       [rsp+20],rax
       mov       rbx,rcx
       xor       esi,esi
       jmp       near ptr M00_L15
M00_L00:
       mov       rcx,1924F800190
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L18
       mov       rcx,1924F800170
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L18
       jmp       near ptr M00_L33
M00_L01:
       mov       r13d,r15d
       jmp       near ptr M00_L20
M00_L02:
       lea       rdx,[rax+0C]
       mov       [rsp+28],rdx
       mov       rdx,[rsp+28]
M00_L03:
       movsxd    rcx,r13d
       lea       rdx,[rdx+rcx*2]
       mov       ecx,ebp
       neg       ecx
       mov       r8d,1
       cmp       ecx,64
       jb        near ptr M00_L09
       mov       r10,19200201B94
M00_L04:
       add       rdx,0FFFFFFFFFFFFFFFC
       add       r8d,0FFFFFFFE
       mov       r9d,ecx
       imul      r9,51EB851F
       shr       r9,25
       imul      r11d,r9d,64
       sub       ecx,r11d
       mov       r11,r10
       shl       ecx,2
       mov       ecx,[r11+rcx]
       mov       [rdx],ecx
       cmp       r9d,64
       mov       ecx,r9d
       jae       short M00_L04
       jmp       short M00_L09
M00_L05:
       mov       r8d,[r14+8]
M00_L06:
       add       rdx,0FFFFFFFFFFFFFFFE
       cmp       ecx,[r14+8]
       jae       near ptr M00_L40
       mov       r8d,ecx
       movzx     r8d,word ptr [r14+r8*2+0C]
       mov       [rdx],r8w
       dec       ecx
       jns       short M00_L06
       jmp       short M00_L11
       nop       dword ptr [rax]
M00_L07:
       mov       rbp,[rbp]
       test      rbp,rbp
       je        near ptr M00_L38
       xor       edx,edx
       xor       r14d,r14d
       jmp       near ptr M00_L14
M00_L08:
       dec       r8d
       mov       r10d,0CCCCCCCD
       mov       r9d,ecx
       imul      r10,r9
       shr       r10,23
       lea       r9d,[r10+r10*4]
       add       r9d,r9d
       mov       r11d,ecx
       sub       r11d,r9d
       mov       ecx,r10d
       add       rdx,0FFFFFFFFFFFFFFFE
       add       r11d,30
       mov       [rdx],r11w
M00_L09:
       test      ecx,ecx
       jne       short M00_L08
       test      r8d,r8d
       jg        short M00_L08
       mov       ecx,[r14+8]
       dec       ecx
       js        short M00_L11
       cmp       [r14+8],ecx
       jle       near ptr M00_L05
       nop       dword ptr [rax]
M00_L10:
       add       rdx,0FFFFFFFFFFFFFFFE
       mov       r8d,ecx
       movzx     r8d,word ptr [r14+r8*2+0C]
       mov       [rdx],r8w
       dec       ecx
       jns       short M00_L10
M00_L11:
       xor       edx,edx
       mov       [rsp+28],rdx
M00_L12:
       mov       rdx,rax
M00_L13:
       test      rdx,rdx
       je        near ptr M00_L39
       xor       ebp,ebp
       mov       r14d,1
M00_L14:
       cmp       esi,[rdi+8]
       jae       near ptr M00_L40
       lea       rcx,[rsi+rsi*2]
       lea       rdi,[rdi+rcx*8+10]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rdi+8]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rdi+10],r14b
       inc       esi
       cmp       esi,400
       jge       near ptr M00_L31
M00_L15:
       mov       rdi,[rbx+38]
       mov       rcx,[rbx+10]
       cmp       esi,[rcx+8]
       jae       near ptr M00_L40
       mov       rax,rsi
       shl       rax,4
       lea       rbp,[rcx+rax+10]
       mov       rcx,1924F8008C8
       mov       rax,[rcx]
       test      rax,rax
       je        near ptr M00_L21
M00_L16:
       cmp       byte ptr [rbp+0C],0
       je        near ptr M00_L07
       mov       ebp,[rbp+8]
       mov       rdx,7FF8145A2D60
       cmp       [rax+18],rdx
       jne       near ptr M00_L37
       test      ebp,ebp
       jge       near ptr M00_L22
       mov       rcx,gs:[58]
       mov       rcx,[rcx+48]
       cmp       dword ptr [rcx+238],2
       jle       near ptr M00_L32
       mov       rcx,[rcx+240]
       mov       rax,[rcx+10]
       test      rax,rax
       je        near ptr M00_L32
M00_L17:
       mov       rcx,[rax+10]
       test      rcx,rcx
       je        near ptr M00_L00
M00_L18:
       cmp       byte ptr [rcx+61],0
       jne       near ptr M00_L34
       mov       rax,[rcx+18]
       test      rax,rax
       je        near ptr M00_L34
M00_L19:
       mov       r14,[rax+28]
       mov       edx,ebp
       neg       edx
       mov       ecx,edx
       or        ecx,1
       lzcnt     ecx,ecx
       xor       ecx,1F
       mov       r15d,edx
       mov       edx,ecx
       mov       rcx,7FF87305C3D8
       add       r15,[rcx+rdx*8]
       sar       r15,20
       cmp       r15d,1
       jg        near ptr M00_L01
       mov       r13d,1
M00_L20:
       add       r13d,[r14+8]
       movsxd    rdx,r13d
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       test      rax,rax
       jne       near ptr M00_L02
       xor       edx,edx
       jmp       near ptr M00_L03
M00_L21:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[System.String, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,1924F8008B0
       mov       rdx,[rcx]
       lea       rcx,[r14+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145A2D60
       mov       [r14+18],rcx
       mov       rcx,1924F8008C8
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r14
       jmp       near ptr M00_L16
M00_L22:
       cmp       ebp,12C
       jb        near ptr M00_L29
       mov       edx,ebp
       or        edx,1
       lzcnt     edx,edx
       xor       edx,1F
       mov       r14d,ebp
       mov       rcx,7FF87305C3D8
       add       r14,[rcx+rdx*8]
       sar       r14,20
       movsxd    rdx,r14d
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       test      rax,rax
       je        near ptr M00_L27
       lea       rcx,[rax+0C]
       mov       [rsp+20],rcx
       mov       rcx,[rsp+20]
M00_L23:
       movsxd    rdx,r14d
       lea       rcx,[rcx+rdx*2]
       cmp       ebp,0A
       jb        short M00_L28
       cmp       ebp,64
       jb        short M00_L25
       mov       r14,19200201B94
M00_L24:
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       edx,ebp
       imul      rdx,51EB851F
       shr       rdx,25
       imul      r8d,edx,64
       sub       ebp,r8d
       mov       r8,r14
       shl       ebp,2
       mov       r10d,ebp
       mov       r8d,[r8+r10]
       mov       [rcx],r8d
       cmp       edx,64
       mov       ebp,edx
       jae       short M00_L24
M00_L25:
       cmp       ebp,0A
       jb        short M00_L28
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       r14,19200201B94
       lea       edx,[rbp*4]
       mov       edx,[r14+rdx]
       mov       [rcx],edx
M00_L26:
       xor       ecx,ecx
       mov       [rsp+20],rcx
       jmp       short M00_L30
M00_L27:
       xor       eax,eax
       xor       ecx,ecx
       jmp       short M00_L23
M00_L28:
       lea       edx,[rbp+30]
       mov       [rcx-2],dx
       jmp       short M00_L26
M00_L29:
       mov       rcx,1924F800930
       mov       rcx,[rcx]
       mov       eax,ebp
       mov       rax,[rcx+rax*8+10]
       test      rax,rax
       je        short M00_L36
M00_L30:
       jmp       near ptr M00_L12
M00_L31:
       add       rsp,30
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       ret
M00_L32:
       mov       ecx,2
       call      qword ptr [7FF81462C4B0]; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       jmp       near ptr M00_L17
M00_L33:
       call      qword ptr [7FF81424D4B8]; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       mov       rcx,rax
       jmp       near ptr M00_L18
M00_L34:
       mov       rdx,19200201518
       mov       rax,[rcx]
       mov       rax,[rax+50]
       call      qword ptr [rax]
       mov       rdx,rax
       test      rdx,rdx
       je        short M00_L35
       mov       rcx,offset MT_System.Globalization.NumberFormatInfo
       cmp       [rdx],rcx
       je        short M00_L35
       mov       rdx,rax
       call      qword ptr [7FF814246328]; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       int       3
M00_L35:
       mov       rax,rdx
       jmp       near ptr M00_L19
M00_L36:
       mov       ecx,ebp
       call      qword ptr [7FF8145A6E50]; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
       jmp       short M00_L30
M00_L37:
       mov       edx,ebp
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       rdx,rax
       jmp       near ptr M00_L13
M00_L38:
       mov       ecx,460
       mov       rdx,7FF8145956B8
       call      qword ptr [7FF81424F210]
       mov       rcx,rax
       call      qword ptr [7FF8146267A8]
       int       3
M00_L39:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,317
       mov       rdx,7FF8145956B8
       call      qword ptr [7FF81424F210]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8145171B0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M00_L40:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 1266
```
```assembly
; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CE1D18]; Precode of System.Threading.Thread.GetThreadStaticsBase()
       mov       ecx,ebx
       and       ecx,0FFFFFF
       mov       edx,ecx
       mov       r8d,ebx
       sar       r8d,18
       jne       short M01_L01
       cmp       [rax],ecx
       jle       short M01_L03
       mov       rax,[rax+8]
       cmp       [rax],al
       add       edx,0FFFFFFFE
       movsxd    rcx,edx
       mov       rax,[rax+rcx*8+10]
       test      rax,rax
       je        short M01_L03
M01_L00:
       add       rsp,20
       pop       rbx
       ret
M01_L01:
       mov       ecx,ebx
       sar       ecx,18
       cmp       ecx,2
       jne       short M01_L02
       movsxd    rcx,edx
       add       rax,rcx
       jmp       short M01_L00
M01_L02:
       cmp       [rax+4],edx
       jle       short M01_L03
       mov       rcx,[rax+10]
       movsxd    rax,edx
       mov       rcx,[rcx+rax*8]
       test      rcx,rcx
       je        short M01_L03
       mov       rax,[rcx]
       test      rax,rax
       je        short M01_L03
       jmp       short M01_L00
M01_L03:
       mov       ecx,ebx
       lea       rax,[System.Runtime.CompilerServices.StaticsHelpers.GetGCThreadStaticsByIndexSlow(Int32)]
       add       rsp,20
       pop       rbx
       jmp       qword ptr [rax]
; Total bytes of code 130
```
```assembly
; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       push      rsi
       push      rbx
       sub       rsp,28
       call      qword ptr [7FF873CC97E8]
       mov       rbx,rax
       mov       rsi,rbx
       call      qword ptr [7FF873CE0A28]
       mov       rdx,rax
       test      rsi,rsi
       je        short M02_L00
       mov       rcx,rsi
       xor       r8d,r8d
       call      qword ptr [7FF873CE1BC8]
       mov       rax,[rbx]
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M02_L00:
       call      qword ptr [7FF873CDF410]
       int       3
; Total bytes of code 61
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       test      rdx,rdx
       je        short M03_L00
       cmp       [rdx],rcx
       jne       short M03_L01
M03_L00:
       mov       rax,rdx
       ret
M03_L01:
       jmp       qword ptr [7FF814414D20]; System.Runtime.CompilerServices.CastHelpers.ChkCastClassSpecial(Void*, System.Object)
; Total bytes of code 20
```
```assembly
; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CC9700]
       mov       rsi,[rax]
       mov       ecx,ebx
       call      qword ptr [7FF873CDD418]; Precode of System.Number.UInt32ToDecStr_NoSmallNumberCheck(UInt32)
       mov       rdi,rax
       cmp       ebx,[rsi+8]
       jae       short M04_L00
       mov       ecx,ebx
       lea       rcx,[rsi+rcx*8+10]
       mov       rdx,rdi
       call      qword ptr [7FF873CC8FE8]; CORINFO_HELP_ASSIGN_REF
       mov       rax,rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M04_L00:
       call      qword ptr [7FF873CC8FD8]; Precode of Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException()
       int       3
; Total bytes of code 68
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Map_ChainedTransforms()
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,40
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rsp+20],ymm4
       mov       rbx,rcx
       xor       esi,esi
       jmp       near ptr M00_L29
M00_L00:
       mov       rcx,[rbp]
       test      rcx,rcx
       je        near ptr M00_L50
       xor       edx,edx
       xor       eax,eax
       jmp       near ptr M00_L32
M00_L01:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       mov       rcx,246EE4008B0
       mov       rdx,[rcx]
       lea       rcx,[rbp+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145D2F40
       mov       [rbp+18],rcx
       mov       rcx,246EE4008D8
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,rbp
       jmp       near ptr M00_L33
M00_L02:
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       edx,eax
       jmp       near ptr M00_L34
M00_L03:
       mov       rcx,[rsp+30]
       test      rcx,rcx
       je        near ptr M00_L50
       xor       edx,edx
       xor       eax,eax
       jmp       near ptr M00_L35
M00_L04:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[System.String, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       mov       rcx,246EE4008B0
       mov       rdx,[rcx]
       lea       rcx,[rbp+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145D2F58
       mov       [rbp+18],rcx
       mov       rcx,246EE4008E0
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,rbp
       jmp       near ptr M00_L36
M00_L05:
       cmp       ebp,12C
       jb        near ptr M00_L12
       mov       edx,ebp
       or        edx,1
       lzcnt     edx,edx
       xor       edx,1F
       mov       r14d,ebp
       mov       rcx,7FF87305C3D8
       add       r14,[rcx+rdx*8]
       sar       r14,20
       movsxd    rdx,r14d
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       test      rax,rax
       je        near ptr M00_L10
       lea       rcx,[rax+0C]
       mov       [rsp+20],rcx
       mov       rcx,[rsp+20]
M00_L06:
       movsxd    rdx,r14d
       lea       rcx,[rcx+rdx*2]
       cmp       ebp,0A
       jb        short M00_L11
       nop       dword ptr [rax]
       cmp       ebp,64
       jb        short M00_L08
       mov       r14,246803D1B94
M00_L07:
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       edx,ebp
       imul      rdx,51EB851F
       shr       rdx,25
       imul      r8d,edx,64
       sub       ebp,r8d
       mov       r8,r14
       shl       ebp,2
       mov       r10d,ebp
       mov       r8d,[r8+r10]
       mov       [rcx],r8d
       cmp       edx,64
       mov       ebp,edx
       jae       short M00_L07
M00_L08:
       cmp       ebp,0A
       jb        short M00_L11
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       r14,246803D1B94
       lea       edx,[rbp*4]
       mov       edx,[r14+rdx]
       mov       [rcx],edx
M00_L09:
       xor       ecx,ecx
       mov       [rsp+20],rcx
       jmp       short M00_L13
M00_L10:
       xor       eax,eax
       xor       ecx,ecx
       jmp       short M00_L06
       nop       word ptr [rax+rax]
M00_L11:
       lea       edx,[rbp+30]
       mov       [rcx-2],dx
       jmp       short M00_L09
M00_L12:
       mov       rcx,246EE400930
       mov       rcx,[rcx]
       mov       eax,ebp
       mov       rax,[rcx+rax*8+10]
       test      rax,rax
       je        near ptr M00_L48
M00_L13:
       jmp       near ptr M00_L26
M00_L14:
       mov       rcx,246EE400190
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L38
       mov       rcx,246EE400170
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L38
       jmp       near ptr M00_L45
M00_L15:
       mov       r13d,r15d
       jmp       near ptr M00_L40
M00_L16:
       lea       rdx,[rax+0C]
       mov       [rsp+28],rdx
       mov       rdx,[rsp+28]
M00_L17:
       movsxd    rcx,r13d
       lea       rdx,[rdx+rcx*2]
       mov       ecx,ebp
       neg       ecx
       mov       r8d,1
       cmp       ecx,64
       jb        near ptr M00_L23
       mov       r10,246803D1B94
M00_L18:
       add       rdx,0FFFFFFFFFFFFFFFC
       add       r8d,0FFFFFFFE
       mov       r9d,ecx
       imul      r9,51EB851F
       shr       r9,25
       imul      r11d,r9d,64
       sub       ecx,r11d
       mov       r11,r10
       shl       ecx,2
       mov       ecx,[r11+rcx]
       mov       [rdx],ecx
       cmp       r9d,64
       mov       ecx,r9d
       jae       short M00_L18
       jmp       short M00_L23
M00_L19:
       mov       r8d,[r14+8]
M00_L20:
       add       rdx,0FFFFFFFFFFFFFFFE
       cmp       ecx,[r14+8]
       jae       near ptr M00_L52
       mov       r8d,ecx
       movzx     r8d,word ptr [r14+r8*2+0C]
       mov       [rdx],r8w
       dec       ecx
       jns       short M00_L20
       jmp       short M00_L25
       nop       dword ptr [rax]
M00_L21:
       mov       rbp,[rsp+30]
       test      rbp,rbp
       je        near ptr M00_L50
       xor       edx,edx
       xor       r14d,r14d
       jmp       short M00_L28
       nop       dword ptr [rax]
M00_L22:
       dec       r8d
       mov       r10d,0CCCCCCCD
       mov       r9d,ecx
       imul      r10,r9
       shr       r10,23
       lea       r9d,[r10+r10*4]
       add       r9d,r9d
       mov       r11d,ecx
       sub       r11d,r9d
       mov       ecx,r10d
       add       rdx,0FFFFFFFFFFFFFFFE
       add       r11d,30
       mov       [rdx],r11w
M00_L23:
       test      ecx,ecx
       jne       short M00_L22
       test      r8d,r8d
       jg        short M00_L22
       mov       ecx,[r14+8]
       dec       ecx
       js        short M00_L25
       cmp       [r14+8],ecx
       jle       near ptr M00_L19
M00_L24:
       add       rdx,0FFFFFFFFFFFFFFFE
       mov       r8d,ecx
       movzx     r8d,word ptr [r14+r8*2+0C]
       mov       [rdx],r8w
       dec       ecx
       jns       short M00_L24
M00_L25:
       xor       edx,edx
       mov       [rsp+28],rdx
M00_L26:
       mov       rdx,rax
M00_L27:
       test      rdx,rdx
       je        near ptr M00_L51
       xor       ebp,ebp
       mov       r14d,1
M00_L28:
       cmp       esi,[rdi+8]
       jae       near ptr M00_L52
       lea       rcx,[rsi+rsi*2]
       lea       rdi,[rdi+rcx*8+10]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rdi+8]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rdi+10],r14b
       inc       esi
       cmp       esi,400
       jge       near ptr M00_L43
M00_L29:
       mov       rdi,[rbx+38]
       mov       rcx,[rbx+10]
       cmp       esi,[rcx+8]
       jae       near ptr M00_L52
       mov       rax,rsi
       shl       rax,4
       lea       rbp,[rcx+rax+10]
       mov       rcx,246EE4008D0
       mov       rax,[rcx]
       test      rax,rax
       je        near ptr M00_L41
M00_L30:
       cmp       byte ptr [rbp+0C],0
       je        near ptr M00_L00
       mov       edx,[rbp+8]
       mov       rcx,7FF8145D2F40
       cmp       [rax+18],rcx
       jne       near ptr M00_L42
       add       edx,0A
M00_L31:
       xor       ecx,ecx
       mov       eax,1
M00_L32:
       mov       [rsp+30],rcx
       mov       [rsp+38],edx
       mov       [rsp+3C],al
       mov       rcx,246EE4008D8
       mov       rax,[rcx]
       test      rax,rax
       je        near ptr M00_L01
M00_L33:
       cmp       byte ptr [rsp+3C],0
       je        near ptr M00_L03
       mov       edx,[rsp+38]
       mov       rcx,7FF8145D2F40
       cmp       [rax+18],rcx
       jne       near ptr M00_L02
       add       edx,0A
M00_L34:
       xor       ecx,ecx
       mov       eax,1
M00_L35:
       mov       [rsp+30],rcx
       mov       [rsp+38],edx
       mov       [rsp+3C],al
       mov       rcx,246EE4008E0
       mov       rax,[rcx]
       test      rax,rax
       je        near ptr M00_L04
M00_L36:
       cmp       byte ptr [rsp+3C],0
       je        near ptr M00_L21
       mov       ebp,[rsp+38]
       mov       rdx,7FF8145D2F58
       cmp       [rax+18],rdx
       jne       near ptr M00_L49
       test      ebp,ebp
       jge       near ptr M00_L05
       mov       rcx,gs:[58]
       mov       rcx,[rcx+48]
       cmp       dword ptr [rcx+238],2
       jle       near ptr M00_L44
       mov       rcx,[rcx+240]
       mov       rax,[rcx+10]
       test      rax,rax
       je        near ptr M00_L44
M00_L37:
       mov       rcx,[rax+10]
       test      rcx,rcx
       je        near ptr M00_L14
M00_L38:
       cmp       byte ptr [rcx+61],0
       jne       near ptr M00_L46
       mov       rax,[rcx+18]
       test      rax,rax
       je        near ptr M00_L46
M00_L39:
       mov       r14,[rax+28]
       mov       edx,ebp
       neg       edx
       mov       ecx,edx
       or        ecx,1
       lzcnt     ecx,ecx
       xor       ecx,1F
       mov       r15d,edx
       mov       edx,ecx
       mov       rcx,7FF87305C3D8
       add       r15,[rcx+rdx*8]
       sar       r15,20
       cmp       r15d,1
       jg        near ptr M00_L15
       mov       r13d,1
M00_L40:
       add       r13d,[r14+8]
       movsxd    rdx,r13d
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       test      rax,rax
       jne       near ptr M00_L16
       xor       edx,edx
       jmp       near ptr M00_L17
M00_L41:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,246EE4008B0
       mov       rdx,[rcx]
       lea       rcx,[r14+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145D2F28
       mov       [r14+18],rcx
       mov       rcx,246EE4008D0
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r14
       jmp       near ptr M00_L30
M00_L42:
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       edx,eax
       jmp       near ptr M00_L31
M00_L43:
       add       rsp,40
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       ret
M00_L44:
       mov       ecx,2
       call      qword ptr [7FF81465C4B0]; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       jmp       near ptr M00_L37
M00_L45:
       call      qword ptr [7FF81427D4B8]; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       mov       rcx,rax
       jmp       near ptr M00_L38
M00_L46:
       mov       rdx,246803D1518
       mov       rax,[rcx]
       mov       rax,[rax+50]
       call      qword ptr [rax]
       mov       rdx,rax
       test      rdx,rdx
       je        short M00_L47
       mov       rcx,offset MT_System.Globalization.NumberFormatInfo
       cmp       [rdx],rcx
       je        short M00_L47
       mov       rdx,rax
       call      qword ptr [7FF814276328]; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       int       3
M00_L47:
       mov       rax,rdx
       jmp       near ptr M00_L39
M00_L48:
       mov       ecx,ebp
       call      qword ptr [7FF8145D6E50]; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
       jmp       near ptr M00_L13
M00_L49:
       mov       edx,ebp
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       rdx,rax
       jmp       near ptr M00_L27
M00_L50:
       mov       ecx,460
       mov       rdx,7FF8145C56B8
       call      qword ptr [7FF81427F210]
       mov       rcx,rax
       call      qword ptr [7FF8146567A8]
       int       3
M00_L51:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,317
       mov       rdx,7FF8145C56B8
       call      qword ptr [7FF81427F210]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8145471B0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M00_L52:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 1672
```
```assembly
; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CE1D18]; Precode of System.Threading.Thread.GetThreadStaticsBase()
       mov       ecx,ebx
       and       ecx,0FFFFFF
       mov       edx,ecx
       mov       r8d,ebx
       sar       r8d,18
       jne       short M01_L01
       cmp       [rax],ecx
       jle       short M01_L03
       mov       rax,[rax+8]
       cmp       [rax],al
       add       edx,0FFFFFFFE
       movsxd    rcx,edx
       mov       rax,[rax+rcx*8+10]
       test      rax,rax
       je        short M01_L03
M01_L00:
       add       rsp,20
       pop       rbx
       ret
M01_L01:
       mov       ecx,ebx
       sar       ecx,18
       cmp       ecx,2
       jne       short M01_L02
       movsxd    rcx,edx
       add       rax,rcx
       jmp       short M01_L00
M01_L02:
       cmp       [rax+4],edx
       jle       short M01_L03
       mov       rcx,[rax+10]
       movsxd    rax,edx
       mov       rcx,[rcx+rax*8]
       test      rcx,rcx
       je        short M01_L03
       mov       rax,[rcx]
       test      rax,rax
       je        short M01_L03
       jmp       short M01_L00
M01_L03:
       mov       ecx,ebx
       lea       rax,[System.Runtime.CompilerServices.StaticsHelpers.GetGCThreadStaticsByIndexSlow(Int32)]
       add       rsp,20
       pop       rbx
       jmp       qword ptr [rax]
; Total bytes of code 130
```
```assembly
; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       push      rsi
       push      rbx
       sub       rsp,28
       call      qword ptr [7FF873CC97E8]
       mov       rbx,rax
       mov       rsi,rbx
       call      qword ptr [7FF873CE0A28]
       mov       rdx,rax
       test      rsi,rsi
       je        short M02_L00
       mov       rcx,rsi
       xor       r8d,r8d
       call      qword ptr [7FF873CE1BC8]
       mov       rax,[rbx]
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M02_L00:
       call      qword ptr [7FF873CDF410]
       int       3
; Total bytes of code 61
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       test      rdx,rdx
       je        short M03_L00
       cmp       [rdx],rcx
       jne       short M03_L01
M03_L00:
       mov       rax,rdx
       ret
M03_L01:
       jmp       qword ptr [7FF814444D20]; System.Runtime.CompilerServices.CastHelpers.ChkCastClassSpecial(Void*, System.Object)
; Total bytes of code 20
```
```assembly
; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CC9700]
       mov       rsi,[rax]
       mov       ecx,ebx
       call      qword ptr [7FF873CDD418]; Precode of System.Number.UInt32ToDecStr_NoSmallNumberCheck(UInt32)
       mov       rdi,rax
       cmp       ebx,[rsi+8]
       jae       short M04_L00
       mov       ecx,ebx
       lea       rcx,[rsi+rcx*8+10]
       mov       rdx,rdi
       call      qword ptr [7FF873CC8FE8]; CORINFO_HELP_ASSIGN_REF
       mov       rax,rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M04_L00:
       call      qword ptr [7FF873CC8FD8]; Precode of Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException()
       int       3
; Total bytes of code 68
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Bind_SingleOperation()
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       xor       eax,eax
       mov       [rsp+28],rax
       mov       [rsp+30],rax
       mov       rbx,rcx
       xor       esi,esi
       jmp       short M00_L02
M00_L00:
       mov       r8d,[r14+8]
       mov       rdx,7FF8145D2FA0
       cmp       [rax+18],rdx
       jne       near ptr M00_L06
       add       r8d,r8d
       xor       edx,edx
       mov       [rsp+28],rdx
       mov       [rsp+30],r8d
       mov       byte ptr [rsp+34],1
M00_L01:
       cmp       esi,[rdi+8]
       jae       near ptr M00_L08
       lea       rdi,[rdi+rbp+10]
       mov       rdx,[rsp+28]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       ecx,[rsp+30]
       mov       [rdi+8],ecx
       movzx     ecx,byte ptr [rsp+34]
       mov       [rdi+0C],cl
       inc       esi
       cmp       esi,400
       jge       short M00_L04
M00_L02:
       mov       rdi,[rbx+30]
       mov       rcx,[rbx+10]
       cmp       esi,[rcx+8]
       jae       near ptr M00_L08
       mov       rbp,rsi
       shl       rbp,4
       lea       r14,[rcx+rbp+10]
       mov       rcx,229C28008E8
       mov       rax,[rcx]
       test      rax,rax
       je        short M00_L05
M00_L03:
       cmp       byte ptr [r14+0C],0
       jne       near ptr M00_L00
       mov       rcx,[r14]
       test      rcx,rcx
       je        near ptr M00_L07
       mov       [rsp+28],rcx
       xor       ecx,ecx
       mov       [rsp+30],ecx
       mov       byte ptr [rsp+34],0
       jmp       near ptr M00_L01
M00_L04:
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M00_L05:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[Pragmatic.Result.Result`2[[System.Int32, System.Private.CoreLib],[Pragmatic.Result.Benchmarks.BenchmarkError, Pragmatic.Result.Benchmarks]], Pragmatic.Result]]
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,229C28008B0
       mov       rdx,[rcx]
       lea       rcx,[r15+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145D2FA0
       mov       [r15+18],rcx
       mov       rcx,229C28008E8
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r15
       jmp       near ptr M00_L03
M00_L06:
       lea       rdx,[rsp+28]
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       jmp       near ptr M00_L01
M00_L07:
       mov       ecx,460
       mov       rdx,7FF8145C56B8
       call      qword ptr [7FF81427F210]
       mov       rcx,rax
       call      qword ptr [7FF8146567A8]
       int       3
M00_L08:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 365
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.VoidResult_Success()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+40]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       mov       r8,rax
       shl       r8,4
       lea       rdx,[rdx+r8+10]
       xor       r8d,r8d
       mov       [rdx],r8
       mov       byte ptr [rdx+8],1
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 57
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.VoidResult_Failure()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       xor       esi,esi
       mov       rdi,[rbx+40]
       mov       rcx,22FEF8008A0
       mov       rbp,[rcx]
       mov       rdx,rbp
       jmp       short M00_L01
M00_L00:
       mov       rdi,[rbx+40]
       mov       rdx,rbp
M00_L01:
       cmp       esi,[rdi+8]
       jae       short M00_L02
       mov       rcx,rsi
       shl       rcx,4
       lea       rdi,[rdi+rcx+10]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       byte ptr [rdi+8],0
       inc       esi
       cmp       esi,400
       jl        short M00_L00
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L02:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 96
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Maybe_CreateSome()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+48]
       mov       r8,[rcx+8]
       cmp       eax,[r8+8]
       jae       short M00_L01
       mov       r10d,eax
       mov       r8d,[r8+r10*4+10]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       lea       rdx,[rdx+r10*8+10]
       mov       [rdx],r8d
       mov       byte ptr [rdx+4],1
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 65
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Maybe_CreateNone()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+48]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       lea       rdx,[rdx+rax*8+10]
       xor       r8d,r8d
       mov       [rdx],r8d
       mov       byte ptr [rdx+4],0
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 50
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Maybe_GetValueOrDefault()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+20]
       xor       edx,edx
       jmp       short M00_L02
M00_L00:
       mov       r8d,[r8]
M00_L01:
       add       eax,r8d
       inc       edx
       cmp       edx,400
       jge       short M00_L03
M00_L02:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L04
       lea       r8,[r8+rdx*8+10]
       cmp       byte ptr [r8+4],0
       jne       short M00_L00
       xor       r8d,r8d
       jmp       short M00_L01
M00_L03:
       add       rsp,28
       ret
M00_L04:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 67
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Maybe_Match()
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       xor       esi,esi
       xor       edi,edi
       jmp       short M00_L02
M00_L00:
       mov       edx,[rbp]
       mov       rcx,7FF8145C34C8
       cmp       [r14+18],rcx
       jne       near ptr M00_L10
M00_L01:
       add       esi,edx
       inc       edi
       cmp       edi,400
       jge       short M00_L06
M00_L02:
       mov       rcx,[rbx+20]
       cmp       edi,[rcx+8]
       jae       near ptr M00_L11
       lea       rbp,[rcx+rdi*8+10]
       mov       rcx,2174C8008F8
       mov       r14,[rcx]
       test      r14,r14
       je        short M00_L07
M00_L03:
       mov       rcx,2174C800900
       mov       rax,[rcx]
       test      rax,rax
       je        short M00_L08
M00_L04:
       cmp       byte ptr [rbp+4],0
       jne       short M00_L00
       mov       rcx,7FF8145C34E0
       cmp       [rax+18],rcx
       jne       near ptr M00_L09
       xor       edx,edx
M00_L05:
       jmp       short M00_L01
M00_L06:
       mov       eax,esi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M00_L07:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,2174C8008B8
       mov       rdx,[rcx]
       lea       rcx,[r14+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145C34C8
       mov       [r14+18],rcx
       mov       rcx,2174C8008F8
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L03
M00_L08:
       mov       rcx,offset MT_System.Func`1[[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,2174C8008B8
       mov       rdx,[rcx]
       lea       rcx,[r15+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145C34E0
       mov       [r15+18],rcx
       mov       rcx,2174C800900
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r15
       jmp       near ptr M00_L04
M00_L09:
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       edx,eax
       jmp       near ptr M00_L05
M00_L10:
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       mov       edx,eax
       jmp       near ptr M00_L01
M00_L11:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 347
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.MultiError_CreateSuccess()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+50]
       mov       r8,[rcx+8]
       cmp       eax,[r8+8]
       jae       short M00_L01
       mov       r10d,eax
       mov       r8d,[r8+r10*4+10]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       lea       r10,[r10+r10*2]
       lea       rdx,[rdx+r10*8+10]
       xor       r10d,r10d
       mov       [rdx],r10
       mov       [rdx+8],r10
       mov       [rdx+10],r8d
       mov       byte ptr [rdx+14],0
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 80
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.MultiError_Match()
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
M00_L00:
       xor       esi,esi
       xor       edi,edi
       jmp       short M00_L03
M00_L01:
       mov       edx,[rbp+10]
       mov       rcx,7FF8145E3738
       cmp       [r14+18],rcx
       jne       near ptr M00_L13
       mov       r13d,edx
M00_L02:
       add       esi,r13d
       inc       edi
       cmp       edi,400
       jge       near ptr M00_L08
M00_L03:
       mov       rcx,[rbx+28]
       cmp       edi,[rcx+8]
       jae       near ptr M00_L17
       lea       rax,[rdi+rdi*2]
       lea       rbp,[rcx+rax*8+10]
       mov       rcx,1A0FE400908
       mov       r14,[rcx]
       test      r14,r14
       je        near ptr M00_L09
M00_L04:
       mov       rcx,1A0FE400910
       mov       r15,[rcx]
       test      r15,r15
       je        near ptr M00_L10
M00_L05:
       mov       rcx,1A0FE400918
       mov       rax,[rcx]
       test      rax,rax
       je        near ptr M00_L11
M00_L06:
       movzx     edx,byte ptr [rbp+14]
       test      edx,edx
       je        near ptr M00_L01
       cmp       edx,2
       ja        near ptr M00_L16
       mov       edx,edx
       lea       rcx,[7FF8143068B8]
       mov       ecx,[rcx+rdx*4]
       lea       r8,[M00_L00]
       add       rcx,r8
       jmp       rcx
       mov       rdx,[rbp]
       mov       rcx,7FF8145E3750
       cmp       [r15+18],rcx
       jne       near ptr M00_L14
       mov       r13d,0FFFFFFFF
M00_L07:
       jmp       near ptr M00_L02
M00_L08:
       mov       eax,esi
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       ret
M00_L09:
       mov       rcx,offset MT_System.Func`2[[System.Int32, System.Private.CoreLib],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,1A0FE4008B8
       mov       rdx,[rcx]
       lea       rcx,[r14+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145E3738
       mov       [r14+18],rcx
       mov       rcx,1A0FE400908
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L04
M00_L10:
       mov       rcx,offset MT_System.Func`2[[Pragmatic.Result.Benchmarks.BenchmarkError, Pragmatic.Result.Benchmarks],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,1A0FE4008B8
       mov       rdx,[rcx]
       lea       rcx,[r15+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145E3750
       mov       [r15+18],rcx
       mov       rcx,1A0FE400910
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L05
M00_L11:
       mov       rcx,offset MT_System.Func`2[[Pragmatic.Result.Benchmarks.BenchmarkError2, Pragmatic.Result.Benchmarks],[System.Int32, System.Private.CoreLib]]
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       mov       rcx,1A0FE4008B8
       mov       rdx,[rcx]
       lea       rcx,[r13+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8145E3768
       mov       [r13+18],rcx
       mov       rcx,1A0FE400918
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r13
       jmp       near ptr M00_L06
       mov       rdx,[rbp+8]
       mov       rcx,7FF8145E3768
       cmp       [rax+18],rcx
       jne       short M00_L15
       mov       r13d,0FFFFFFFE
M00_L12:
       jmp       near ptr M00_L02
M00_L13:
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       mov       r13d,eax
       jmp       near ptr M00_L02
M00_L14:
       mov       rcx,[r15+8]
       call      qword ptr [r15+18]
       mov       r13d,eax
       jmp       near ptr M00_L07
M00_L15:
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       r13d,eax
       jmp       short M00_L12
M00_L16:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,0EF4
       mov       rdx,7FF8145D6510
       call      qword ptr [7FF81428F210]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF814557960]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M00_L17:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 626
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_CreateSuccess()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+78]
       mov       r8,[rcx+8]
       cmp       eax,[r8+8]
       jae       short M00_L01
       mov       r10d,eax
       mov       r8d,[r8+r10*4+10]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       shl       r10,4
       lea       rdx,[rdx+r10+10]
       xor       r10d,r10d
       mov       [rdx],r10
       mov       [rdx+8],r8d
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 72
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_CreateFailure()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rcx,2EBFD0008A0
       mov       rsi,[rcx]
       xor       edi,edi
       nop       word ptr [rax+rax]
M00_L00:
       mov       rcx,[rbx+78]
       mov       rdx,rsi
       cmp       edi,[rcx+8]
       jae       short M00_L01
       mov       rax,rdi
       shl       rax,4
       lea       rbp,[rcx+rax+10]
       mov       rcx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       xor       eax,eax
       mov       [rbp+8],eax
       inc       edi
       cmp       edi,400
       jl        short M00_L00
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 94
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_IsSuccess()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+58]
       xor       edx,edx
       jmp       short M00_L02
M00_L00:
       inc       eax
M00_L01:
       inc       edx
       cmp       edx,400
       jge       short M00_L03
M00_L02:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L04
       mov       r10,rdx
       shl       r10,4
       cmp       qword ptr [r8+r10+10],0
       jne       short M00_L01
       jmp       short M00_L00
M00_L03:
       add       rsp,28
       ret
M00_L04:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 63
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_Value()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+60]
       xor       edx,edx
       nop       dword ptr [rax]
M00_L00:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L01
       mov       r10,rdx
       shl       r10,4
       add       eax,[r8+r10+18]
       inc       edx
       cmp       edx,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 58
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_ValueOrZero()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+58]
       xor       edx,edx
       jmp       short M00_L01
M00_L00:
       add       eax,r8d
       inc       edx
       cmp       edx,400
       jge       short M00_L02
M00_L01:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L03
       mov       r10,rdx
       shl       r10,4
       lea       r8,[r8+r10+10]
       mov       r10,[r8]
       mov       r8d,[r8+8]
       test      r10,r10
       je        short M00_L00
       xor       r8d,r8d
       jmp       short M00_L00
M00_L02:
       add       rsp,28
       ret
M00_L03:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 76
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_Map()
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       xor       eax,eax
       mov       [rsp+30],rax
       mov       [rsp+28],rax
       mov       rbx,rcx
       xor       esi,esi
       jmp       near ptr M00_L19
M00_L00:
       mov       rcx,gs:[58]
       mov       rcx,[rcx+48]
       cmp       dword ptr [rcx+238],2
       jle       near ptr M00_L29
       mov       rcx,[rcx+240]
       mov       rax,[rcx+10]
       test      rax,rax
       je        near ptr M00_L29
M00_L01:
       mov       rcx,[rax+10]
       test      rcx,rcx
       je        short M00_L05
M00_L02:
       cmp       byte ptr [rcx+61],0
       jne       near ptr M00_L31
       mov       rax,[rcx+18]
       test      rax,rax
       je        near ptr M00_L31
M00_L03:
       mov       rbp,[rax+28]
       mov       edx,r14d
       neg       edx
       mov       ecx,edx
       or        ecx,1
       lzcnt     ecx,ecx
       xor       ecx,1F
       mov       r13d,edx
       mov       edx,ecx
       mov       rcx,7FF87305C3D8
       add       r13,[rcx+rdx*8]
       sar       r13,20
       cmp       r13d,1
       jg        short M00_L06
       mov       r12d,1
M00_L04:
       add       r12d,[rbp+8]
       movsxd    rdx,r12d
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       mov       rdx,rax
       test      rdx,rdx
       jne       short M00_L07
       xor       ecx,ecx
       jmp       short M00_L08
M00_L05:
       mov       rcx,2AB1B400190
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L02
       mov       rcx,2AB1B400170
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L02
       jmp       near ptr M00_L30
M00_L06:
       mov       r12d,r13d
       jmp       short M00_L04
M00_L07:
       lea       rcx,[rdx+0C]
       mov       [rsp+30],rcx
       mov       rcx,[rsp+30]
M00_L08:
       movsxd    rax,r12d
       lea       rcx,[rcx+rax*2]
       mov       eax,r14d
       neg       eax
       mov       r8d,1
       cmp       eax,64
       jb        near ptr M00_L14
       mov       r10,2AB00201B94
M00_L09:
       add       rcx,0FFFFFFFFFFFFFFFC
       add       r8d,0FFFFFFFE
       mov       r9d,eax
       imul      r9,51EB851F
       shr       r9,25
       imul      r11d,r9d,64
       sub       eax,r11d
       mov       r11,r10
       shl       eax,2
       mov       eax,[r11+rax]
       mov       [rcx],eax
       cmp       r9d,64
       mov       eax,r9d
       jae       short M00_L09
       jmp       short M00_L14
M00_L10:
       mov       r8d,[rbp+8]
M00_L11:
       add       rcx,0FFFFFFFFFFFFFFFE
       cmp       eax,[rbp+8]
       jae       near ptr M00_L33
       mov       r8d,eax
       movzx     r8d,word ptr [rbp+r8*2+0C]
       mov       [rcx],r8w
       dec       eax
       jns       short M00_L11
       jmp       short M00_L16
M00_L12:
       xor       edx,edx
       jmp       short M00_L18
M00_L13:
       dec       r8d
       mov       r10d,0CCCCCCCD
       mov       r9d,eax
       imul      r10,r9
       shr       r10,23
       lea       r9d,[r10+r10*4]
       add       r9d,r9d
       mov       r11d,eax
       sub       r11d,r9d
       mov       eax,r10d
       add       rcx,0FFFFFFFFFFFFFFFE
       add       r11d,30
       mov       [rcx],r11w
M00_L14:
       test      eax,eax
       jne       short M00_L13
       test      r8d,r8d
       jg        short M00_L13
       mov       eax,[rbp+8]
       dec       eax
       js        short M00_L16
       cmp       [rbp+8],eax
       jle       short M00_L10
       nop       dword ptr [rax+rax]
M00_L15:
       add       rcx,0FFFFFFFFFFFFFFFE
       mov       r8d,eax
       movzx     r8d,word ptr [rbp+r8*2+0C]
       mov       [rcx],r8w
       dec       eax
       jns       short M00_L15
M00_L16:
       xor       ecx,ecx
       mov       [rsp+30],rcx
M00_L17:
       xor       ebp,ebp
M00_L18:
       cmp       esi,[r15+8]
       jae       near ptr M00_L33
       lea       rdi,[r15+rdi+10]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rdi+8]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       inc       esi
       cmp       esi,400
       jge       near ptr M00_L28
M00_L19:
       mov       rdx,[rbx+58]
       cmp       esi,[rdx+8]
       jae       near ptr M00_L33
       mov       rdi,rsi
       shl       rdi,4
       lea       rdx,[rdx+rdi+10]
       mov       rbp,[rdx]
       mov       r14d,[rdx+8]
       mov       r15,[rbx+80]
       test      rbp,rbp
       jne       near ptr M00_L12
       test      r14d,r14d
       jl        near ptr M00_L00
       cmp       r14d,12C
       jb        near ptr M00_L26
       mov       edx,r14d
       or        edx,1
       lzcnt     edx,edx
       xor       edx,1F
       mov       ebp,r14d
       mov       rcx,7FF87305C3D8
       add       rbp,[rcx+rdx*8]
       sar       rbp,20
       movsxd    rdx,ebp
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       test      rax,rax
       je        near ptr M00_L24
       lea       rcx,[rax+0C]
       mov       [rsp+28],rcx
       mov       rcx,[rsp+28]
M00_L20:
       movsxd    rdx,ebp
       lea       rcx,[rcx+rdx*2]
       cmp       r14d,0A
       jb        short M00_L25
       cmp       r14d,64
       jb        short M00_L22
       mov       rbp,2AB00201B94
M00_L21:
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       edx,r14d
       imul      rdx,51EB851F
       shr       rdx,25
       imul      r8d,edx,64
       sub       r14d,r8d
       mov       r8,rbp
       shl       r14d,2
       mov       r10d,r14d
       mov       r8d,[r8+r10]
       mov       [rcx],r8d
       cmp       edx,64
       mov       r14d,edx
       jae       short M00_L21
M00_L22:
       cmp       r14d,0A
       jb        short M00_L25
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       rbp,2AB00201B94
       lea       edx,[r14*4]
       mov       edx,[rdx+rbp]
       mov       [rcx],edx
M00_L23:
       xor       ecx,ecx
       mov       [rsp+28],rcx
       jmp       short M00_L27
M00_L24:
       xor       eax,eax
       xor       ecx,ecx
       jmp       short M00_L20
M00_L25:
       lea       edx,[r14+30]
       mov       [rcx-2],dx
       jmp       short M00_L23
M00_L26:
       mov       rcx,2AB1B400930
       mov       rcx,[rcx]
       mov       eax,r14d
       mov       rax,[rcx+rax*8+10]
       test      rax,rax
       jne       short M00_L27
       mov       ecx,r14d
       call      qword ptr [7FF8145D6E68]; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
M00_L27:
       mov       rdx,rax
       jmp       near ptr M00_L17
M00_L28:
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M00_L29:
       mov       ecx,2
       call      qword ptr [7FF8146569A0]; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       jmp       near ptr M00_L01
M00_L30:
       call      qword ptr [7FF81427D4B8]; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       mov       rcx,rax
       jmp       near ptr M00_L02
M00_L31:
       mov       rdx,2AB00201518
       mov       rax,[rcx]
       mov       rax,[rax+50]
       call      qword ptr [rax]
       mov       rdx,rax
       test      rdx,rdx
       je        short M00_L32
       mov       rcx,offset MT_System.Globalization.NumberFormatInfo
       cmp       [rdx],rcx
       je        short M00_L32
       mov       rdx,rax
       call      qword ptr [7FF814276328]; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       int       3
M00_L32:
       mov       rax,rdx
       jmp       near ptr M00_L03
M00_L33:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 997
```
```assembly
; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CC9700]
       mov       rsi,[rax]
       mov       ecx,ebx
       call      qword ptr [7FF873CDD418]; Precode of System.Number.UInt32ToDecStr_NoSmallNumberCheck(UInt32)
       mov       rdi,rax
       cmp       ebx,[rsi+8]
       jae       short M01_L00
       mov       ecx,ebx
       lea       rcx,[rsi+rcx*8+10]
       mov       rdx,rdi
       call      qword ptr [7FF873CC8FE8]; CORINFO_HELP_ASSIGN_REF
       mov       rax,rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M01_L00:
       call      qword ptr [7FF873CC8FD8]; Precode of Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException()
       int       3
; Total bytes of code 68
```
```assembly
; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CE1D18]; Precode of System.Threading.Thread.GetThreadStaticsBase()
       mov       ecx,ebx
       and       ecx,0FFFFFF
       mov       edx,ecx
       mov       r8d,ebx
       sar       r8d,18
       jne       short M02_L01
       cmp       [rax],ecx
       jle       short M02_L03
       mov       rax,[rax+8]
       cmp       [rax],al
       add       edx,0FFFFFFFE
       movsxd    rcx,edx
       mov       rax,[rax+rcx*8+10]
       test      rax,rax
       je        short M02_L03
M02_L00:
       add       rsp,20
       pop       rbx
       ret
M02_L01:
       mov       ecx,ebx
       sar       ecx,18
       cmp       ecx,2
       jne       short M02_L02
       movsxd    rcx,edx
       add       rax,rcx
       jmp       short M02_L00
M02_L02:
       cmp       [rax+4],edx
       jle       short M02_L03
       mov       rcx,[rax+10]
       movsxd    rax,edx
       mov       rcx,[rcx+rax*8]
       test      rcx,rcx
       je        short M02_L03
       mov       rax,[rcx]
       test      rax,rax
       je        short M02_L03
       jmp       short M02_L00
M02_L03:
       mov       ecx,ebx
       lea       rax,[System.Runtime.CompilerServices.StaticsHelpers.GetGCThreadStaticsByIndexSlow(Int32)]
       add       rsp,20
       pop       rbx
       jmp       qword ptr [rax]
; Total bytes of code 130
```
```assembly
; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       push      rsi
       push      rbx
       sub       rsp,28
       call      qword ptr [7FF873CC97E8]
       mov       rbx,rax
       mov       rsi,rbx
       call      qword ptr [7FF873CE0A28]
       mov       rdx,rax
       test      rsi,rsi
       je        short M03_L00
       mov       rcx,rsi
       xor       r8d,r8d
       call      qword ptr [7FF873CE1BC8]
       mov       rax,[rbx]
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M03_L00:
       call      qword ptr [7FF873CDF410]
       int       3
; Total bytes of code 61
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       test      rdx,rdx
       je        short M04_L00
       cmp       [rdx],rcx
       jne       short M04_L01
M04_L00:
       mov       rax,rdx
       ret
M04_L01:
       jmp       qword ptr [7FF814444D20]; System.Runtime.CompilerServices.CastHelpers.ChkCastClassSpecial(Void*, System.Object)
; Total bytes of code 20
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_MapChained()
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       xor       eax,eax
       mov       [rsp+30],rax
       mov       [rsp+28],rax
       mov       rbx,rcx
       xor       esi,esi
       jmp       near ptr M00_L06
M00_L00:
       dec       r8d
       mov       r10d,0CCCCCCCD
       mov       r9d,eax
       imul      r10,r9
       shr       r10,23
       lea       r9d,[r10+r10*4]
       add       r9d,r9d
       mov       r11d,eax
       sub       r11d,r9d
       mov       eax,r10d
       add       rcx,0FFFFFFFFFFFFFFFE
       add       r11d,30
       mov       [rcx],r11w
M00_L01:
       test      eax,eax
       jne       short M00_L00
       test      r8d,r8d
       jg        short M00_L00
       mov       eax,[r15+8]
       dec       eax
       js        short M00_L03
       cmp       [r15+8],eax
       jle       near ptr M00_L27
       nop       dword ptr [rax]
       nop       dword ptr [rax+rax]
M00_L02:
       add       rcx,0FFFFFFFFFFFFFFFE
       mov       r8d,eax
       movzx     r8d,word ptr [r15+r8*2+0C]
       mov       [rcx],r8w
       dec       eax
       jns       short M00_L02
M00_L03:
       xor       ecx,ecx
       mov       [rsp+30],rcx
M00_L04:
       xor       ebp,ebp
M00_L05:
       cmp       esi,[r14+8]
       jae       near ptr M00_L34
       lea       rdi,[r14+rdi+10]
       mov       rcx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rdi+8]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       inc       esi
       cmp       esi,400
       jge       near ptr M00_L12
M00_L06:
       mov       rdx,[rbx+58]
       cmp       esi,[rdx+8]
       jae       near ptr M00_L34
       mov       rdi,rsi
       shl       rdi,4
       lea       rdx,[rdx+rdi+10]
       mov       rbp,[rdx]
       mov       edx,[rdx+8]
       mov       r14,[rbx+80]
       test      rbp,rbp
       jne       near ptr M00_L29
       lea       ebp,[rdx*2+0A]
       test      ebp,ebp
       jl        near ptr M00_L17
       cmp       ebp,12C
       jb        near ptr M00_L15
       mov       edx,ebp
       or        edx,1
       lzcnt     edx,edx
       xor       edx,1F
       mov       r15d,ebp
       mov       rcx,7FF87305C3D8
       add       r15,[rcx+rdx*8]
       sar       r15,20
       movsxd    rdx,r15d
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       test      rax,rax
       je        near ptr M00_L13
       lea       rcx,[rax+0C]
       mov       [rsp+28],rcx
       mov       rcx,[rsp+28]
M00_L07:
       movsxd    rdx,r15d
       lea       rcx,[rcx+rdx*2]
       cmp       ebp,0A
       jb        near ptr M00_L14
       cmp       ebp,64
       jb        short M00_L09
       mov       r15,2C400201B94
M00_L08:
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       edx,ebp
       imul      rdx,51EB851F
       shr       rdx,25
       imul      r8d,edx,64
       sub       ebp,r8d
       mov       r8,r15
       shl       ebp,2
       mov       r10d,ebp
       mov       r8d,[r8+r10]
       mov       [rcx],r8d
       cmp       edx,64
       mov       ebp,edx
       jae       short M00_L08
M00_L09:
       cmp       ebp,0A
       jb        short M00_L14
       add       rcx,0FFFFFFFFFFFFFFFC
       mov       r15,2C400201B94
       lea       edx,[rbp*4]
       mov       edx,[r15+rdx]
       mov       [rcx],edx
M00_L10:
       xor       ecx,ecx
       mov       [rsp+28],rcx
M00_L11:
       mov       rdx,rax
       jmp       near ptr M00_L04
M00_L12:
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M00_L13:
       xor       eax,eax
       xor       ecx,ecx
       jmp       near ptr M00_L07
M00_L14:
       lea       edx,[rbp+30]
       mov       [rcx-2],dx
       jmp       short M00_L10
M00_L15:
       mov       rcx,2C456C00930
       mov       rcx,[rcx]
       mov       eax,ebp
       mov       rax,[rcx+rax*8+10]
       test      rax,rax
       jne       short M00_L16
       mov       ecx,ebp
       call      qword ptr [7FF8145B6E68]; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
M00_L16:
       jmp       short M00_L11
M00_L17:
       mov       rcx,gs:[58]
       mov       rcx,[rcx+48]
       cmp       dword ptr [rcx+238],2
       jle       near ptr M00_L30
       mov       rcx,[rcx+240]
       mov       rax,[rcx+10]
       test      rax,rax
       je        near ptr M00_L30
M00_L18:
       mov       rcx,[rax+10]
       test      rcx,rcx
       je        short M00_L22
M00_L19:
       cmp       byte ptr [rcx+61],0
       jne       near ptr M00_L32
       mov       rax,[rcx+18]
       test      rax,rax
       je        near ptr M00_L32
M00_L20:
       mov       r15,[rax+28]
       mov       edx,ebp
       neg       edx
       mov       ecx,edx
       or        ecx,1
       lzcnt     ecx,ecx
       xor       ecx,1F
       mov       r13d,edx
       mov       edx,ecx
       mov       rcx,7FF87305C3D8
       add       r13,[rcx+rdx*8]
       sar       r13,20
       cmp       r13d,1
       jg        short M00_L23
       mov       r12d,1
M00_L21:
       add       r12d,[r15+8]
       movsxd    rdx,r12d
       mov       rcx,offset MT_System.String
       call      00007FF873EF52E0
       mov       rdx,rax
       test      rdx,rdx
       jne       short M00_L24
       xor       ecx,ecx
       jmp       short M00_L25
M00_L22:
       mov       rcx,2C456C00190
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L19
       mov       rcx,2C456C00170
       mov       rcx,[rcx]
       test      rcx,rcx
       jne       near ptr M00_L19
       jmp       near ptr M00_L31
M00_L23:
       mov       r12d,r13d
       jmp       short M00_L21
M00_L24:
       lea       rcx,[rdx+0C]
       mov       [rsp+30],rcx
       mov       rcx,[rsp+30]
M00_L25:
       movsxd    rax,r12d
       lea       rcx,[rcx+rax*2]
       mov       eax,ebp
       neg       eax
       mov       r8d,1
       cmp       eax,64
       jb        near ptr M00_L01
       mov       r10,2C400201B94
M00_L26:
       add       rcx,0FFFFFFFFFFFFFFFC
       add       r8d,0FFFFFFFE
       mov       r9d,eax
       imul      r9,51EB851F
       shr       r9,25
       imul      r11d,r9d,64
       sub       eax,r11d
       mov       r11,r10
       shl       eax,2
       mov       eax,[r11+rax]
       mov       [rcx],eax
       cmp       r9d,64
       mov       eax,r9d
       jae       short M00_L26
       jmp       near ptr M00_L01
M00_L27:
       mov       r8d,[r15+8]
M00_L28:
       add       rcx,0FFFFFFFFFFFFFFFE
       cmp       eax,[r15+8]
       jae       short M00_L34
       mov       r8d,eax
       movzx     r8d,word ptr [r15+r8*2+0C]
       mov       [rcx],r8w
       dec       eax
       jns       short M00_L28
       jmp       near ptr M00_L03
M00_L29:
       xor       edx,edx
       jmp       near ptr M00_L05
M00_L30:
       mov       ecx,2
       call      qword ptr [7FF8146369A0]; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       jmp       near ptr M00_L18
M00_L31:
       call      qword ptr [7FF81425D4B8]; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       mov       rcx,rax
       jmp       near ptr M00_L19
M00_L32:
       mov       rdx,2C400201518
       mov       rax,[rcx]
       mov       rax,[rax+50]
       call      qword ptr [rax]
       mov       rdx,rax
       test      rdx,rdx
       je        short M00_L33
       mov       rcx,offset MT_System.Globalization.NumberFormatInfo
       cmp       [rdx],rcx
       je        short M00_L33
       mov       rdx,rax
       call      qword ptr [7FF814256328]; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       int       3
M00_L33:
       mov       rax,rdx
       jmp       near ptr M00_L20
M00_L34:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 1015
```
```assembly
; System.Number.<UInt32ToDecStrForKnownSmallNumber>g__CreateAndCacheString|50_0(UInt32)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CC9700]
       mov       rsi,[rax]
       mov       ecx,ebx
       call      qword ptr [7FF873CDD418]; Precode of System.Number.UInt32ToDecStr_NoSmallNumberCheck(UInt32)
       mov       rdi,rax
       cmp       ebx,[rsi+8]
       jae       short M01_L00
       mov       ecx,ebx
       lea       rcx,[rsi+rcx*8+10]
       mov       rdx,rdi
       call      qword ptr [7FF873CC8FE8]; CORINFO_HELP_ASSIGN_REF
       mov       rax,rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M01_L00:
       call      qword ptr [7FF873CC8FD8]; Precode of Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException()
       int       3
; Total bytes of code 68
```
```assembly
; System.Runtime.CompilerServices.StaticsHelpers.GetOptimizedGCThreadStaticBase(Int32)
       push      rbx
       sub       rsp,20
       mov       ebx,ecx
       call      qword ptr [7FF873CE1D18]; Precode of System.Threading.Thread.GetThreadStaticsBase()
       mov       ecx,ebx
       and       ecx,0FFFFFF
       mov       edx,ecx
       mov       r8d,ebx
       sar       r8d,18
       jne       short M02_L01
       cmp       [rax],ecx
       jle       short M02_L03
       mov       rax,[rax+8]
       cmp       [rax],al
       add       edx,0FFFFFFFE
       movsxd    rcx,edx
       mov       rax,[rax+rcx*8+10]
       test      rax,rax
       je        short M02_L03
M02_L00:
       add       rsp,20
       pop       rbx
       ret
M02_L01:
       mov       ecx,ebx
       sar       ecx,18
       cmp       ecx,2
       jne       short M02_L02
       movsxd    rcx,edx
       add       rax,rcx
       jmp       short M02_L00
M02_L02:
       cmp       [rax+4],edx
       jle       short M02_L03
       mov       rcx,[rax+10]
       movsxd    rax,edx
       mov       rcx,[rcx+rax*8]
       test      rcx,rcx
       je        short M02_L03
       mov       rax,[rcx]
       test      rax,rax
       je        short M02_L03
       jmp       short M02_L00
M02_L03:
       mov       ecx,ebx
       lea       rax,[System.Runtime.CompilerServices.StaticsHelpers.GetGCThreadStaticsByIndexSlow(Int32)]
       add       rsp,20
       pop       rbx
       jmp       qword ptr [rax]
; Total bytes of code 130
```
```assembly
; System.Globalization.CultureInfo.InitializeUserDefaultCulture()
       push      rsi
       push      rbx
       sub       rsp,28
       call      qword ptr [7FF873CC97E8]
       mov       rbx,rax
       mov       rsi,rbx
       call      qword ptr [7FF873CE0A28]
       mov       rdx,rax
       test      rsi,rsi
       je        short M03_L00
       mov       rcx,rsi
       xor       r8d,r8d
       call      qword ptr [7FF873CE1BC8]
       mov       rax,[rbx]
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M03_L00:
       call      qword ptr [7FF873CDF410]
       int       3
; Total bytes of code 61
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       test      rdx,rdx
       je        short M04_L00
       cmp       [rdx],rcx
       jne       short M04_L01
M04_L00:
       mov       rax,rdx
       ret
M04_L01:
       jmp       qword ptr [7FF814424D20]; System.Runtime.CompilerServices.CastHelpers.ChkCastClassSpecial(Void*, System.Object)
; Total bytes of code 20
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_Bind()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       xor       esi,esi
       jmp       short M00_L02
M00_L00:
       add       edi,edi
       xor       r8d,r8d
M00_L01:
       cmp       esi,[rcx+8]
       jae       short M00_L04
       lea       rbp,[rcx+rdx+10]
       mov       rcx,rbp
       mov       rdx,r8
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbp+8],edi
       inc       esi
       cmp       esi,400
       jge       short M00_L03
M00_L02:
       mov       rcx,[rbx+58]
       cmp       esi,[rcx+8]
       jae       short M00_L04
       mov       rdx,rsi
       shl       rdx,4
       lea       rcx,[rcx+rdx+10]
       mov       rax,[rcx]
       mov       edi,[rcx+8]
       mov       rcx,[rbx+78]
       test      rax,rax
       je        short M00_L00
       mov       r8,rax
       xor       edi,edi
       jmp       short M00_L01
M00_L03:
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L04:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 112
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_VoidSuccess()
       sub       rsp,28
       mov       rax,[rcx+88]
       xor       ecx,ecx
       nop       dword ptr [rax]
M00_L00:
       mov       rdx,rax
       cmp       ecx,[rdx+8]
       jae       short M00_L01
       xor       r8d,r8d
       mov       [rdx+rcx*8+10],r8
       inc       ecx
       cmp       ecx,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 53
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_VoidFailure()
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rbx,[rcx+88]
       mov       rcx,141AEC008A0
       mov       rsi,[rcx]
       xor       edi,edi
       nop       dword ptr [rax]
M00_L00:
       mov       rcx,rbx
       cmp       edi,[rcx+8]
       jae       short M00_L01
       lea       rcx,[rcx+rdi*8+10]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       inc       edi
       cmp       edi,400
       jl        short M00_L00
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 77
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_MaybeSome()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+90]
       mov       r8,[rcx+8]
       cmp       eax,[r8+8]
       jae       short M00_L01
       mov       r10d,eax
       mov       r8d,[r8+r10*4+10]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       lea       rdx,[rdx+r10*8+10]
       mov       [rdx],r8d
       mov       byte ptr [rdx+4],1
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 68
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_MaybeNone()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+90]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       lea       rdx,[rdx+rax*8+10]
       xor       r8d,r8d
       mov       [rdx],r8d
       mov       byte ptr [rdx+4],0
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 53
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_MaybeValueOrZero()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+68]
       xor       edx,edx
       jmp       short M00_L01
M00_L00:
       add       eax,r10d
       inc       edx
       cmp       edx,400
       jge       short M00_L02
M00_L01:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L03
       lea       r8,[r8+rdx*8+10]
       mov       r10d,[r8]
       movzx     r8d,byte ptr [r8+4]
       test      r8d,r8d
       jne       short M00_L00
       xor       r10d,r10d
       jmp       short M00_L00
M00_L02:
       add       rsp,28
       ret
M00_L03:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 70
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_MultiSuccess()
       sub       rsp,28
       xor       eax,eax
M00_L00:
       mov       rdx,[rcx+98]
       mov       r8,[rcx+8]
       cmp       eax,[r8+8]
       jae       short M00_L01
       mov       r10d,eax
       mov       r8d,[r8+r10*4+10]
       cmp       eax,[rdx+8]
       jae       short M00_L01
       lea       r10,[r10+r10*2]
       lea       rdx,[rdx+r10*8+10]
       xor       r10d,r10d
       mov       [rdx],r10
       mov       [rdx+8],r10
       mov       [rdx+10],r8d
       inc       eax
       cmp       eax,400
       jl        short M00_L00
       add       rsp,28
       ret
M00_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 79
```

## .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```assembly
; Pragmatic.Result.Benchmarks.ResultBenchmarks.Twin_MultiMatch()
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+70]
       xor       edx,edx
       jmp       short M00_L01
       xchg      ax,ax
M00_L00:
       add       eax,r8d
       inc       edx
       cmp       edx,400
       jge       short M00_L03
M00_L01:
       mov       r8,rcx
       cmp       edx,[r8+8]
       jae       short M00_L04
       lea       r10,[rdx+rdx*2]
       lea       r8,[r8+r10*8+10]
       mov       r10,[r8]
       mov       r9,[r8+8]
       mov       r8d,[r8+10]
       test      r10,r10
       jne       short M00_L02
       test      r9,r9
       je        short M00_L00
       mov       r8d,0FFFFFFFE
       jmp       short M00_L00
M00_L02:
       mov       r8d,0FFFFFFFF
       jmp       short M00_L00
M00_L03:
       add       rsp,28
       ret
M00_L04:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 95
```

