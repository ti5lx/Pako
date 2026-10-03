! jtty_pako.f90 -- C-callable API for using the WSJT-X JTTY engine from Pako
! (VB.NET via P/Invoke). Wraps lib/jtty from WSJT-X 3.2.0-rc1 (GPLv3).
!
! Copyright (C) 2026 Francisco, TI2LX
! This file is part of Pako and is distributed under the GNU General
! Public License version 3 (or later), the same license as WSJT-X.
!
! All audio is mono, 12000 samples/s. Receive uses 16-bit integer samples;
! transmit returns 32-bit float samples in the range -1..+1.

module jtty_pako
  use, intrinsic :: iso_c_binding
  implicit none
  private

  integer, parameter :: RX_RATE = 12000
  integer, parameter :: NSPS    = 384          ! 31.25 baud
  integer, parameter :: BUF_SECONDS = 30*60    ! same as WSJT-X (NTMAX)
  integer, parameter :: NBUF = BUF_SECONDS*RX_RATE
  integer, parameter :: BATCH = 30, MSGLEN = 80

  integer(c_int16_t), allocatable, save :: buf(:)
  integer, save :: kz = 0

contains

  !> Returns the API version (bump when the interface changes).
  integer(c_int) function jtty_pako_version() bind(C, name='jtty_pako_version')
    jtty_pako_version = 1
  end function jtty_pako_version

  !> Forgets all received audio and partial messages.
  subroutine jtty_pako_reset() bind(C, name='jtty_pako_reset')
    if(.not.allocated(buf)) allocate(buf(NBUF))
    kz = 0
    buf = 0
    call jtty_pako_call_decoder()       ! a shorter buffer makes rjtty restart
  end subroutine jtty_pako_reset

  !> Appends n new samples and runs the decoder on everything received so far.
  !> nfa..nfb: audio search range in Hz (e.g. 200..3000)
  !> f0, ftol: the Rx frequency in Hz and its tolerance (as in WSJT-X)
  !> Returns 1 if the 30-minute buffer filled up and was restarted, else 0.
  integer(c_int) function jtty_pako_feed(samples, n, nfa, nfb, f0, ftol) &
       bind(C, name='jtty_pako_feed')
    integer(c_int), value :: n, nfa, nfb
    real(c_float),  value :: f0, ftol
    integer(c_int16_t), intent(in) :: samples(n)
    integer :: m

    jtty_pako_feed = 0
    if(.not.allocated(buf)) then
       allocate(buf(NBUF))
       buf = 0
    endif
    if(n <= 0) return
    if(kz + n > NBUF) then                 ! buffer full: start a new session
       kz = 0
       call jtty_pako_call_decoder()
       jtty_pako_feed = 1
    endif
    m = min(n, NBUF)
    buf(kz+1:kz+m) = samples(1:m)
    kz = kz + m
    call jtty_pako_call_decoder(nfa, nfb, f0, ftol)
  end function jtty_pako_feed

  subroutine jtty_pako_call_decoder(nfa, nfb, f0, ftol)
    integer, intent(in), optional :: nfa, nfb
    real,    intent(in), optional :: f0, ftol
    integer :: nfa1, nfb1, nsps1, kz1
    real :: f01, ftol1
    nfa1 = 200;  if(present(nfa))  nfa1 = nfa
    nfb1 = 3000; if(present(nfb))  nfb1 = nfb
    f01 = 1500.0; if(present(f0))  f01 = f0
    ftol1 = 50.0; if(present(ftol)) ftol1 = ftol
    nsps1 = NSPS
    kz1 = max(kz, 1)
    call rjtty_sub(buf, kz1, nsps1, nfa1, nfb1, f01, ftol1)
  end subroutine jtty_pako_call_decoder

  !> Collects decoded text that is new or has grown since the last call.
  !> Up to 30 updates per call; call again while the result equals 30.
  !>   text      : count*80 characters, one 80-char block per update
  !>   ids       : message id; the same id is sent again as its text grows
  !>   freq      : audio frequency in Hz
  !>   tstart    : seconds from the start of the buffer to the message start
  !>   complete  : 1 when the message ended (end-of-message received)
  !> Returns the number of updates.
  integer(c_int) function jtty_pako_get_updates(text, ids, freq, tstart, complete) &
       bind(C, name='jtty_pako_get_updates')
    character(kind=c_char), intent(out) :: text(BATCH*MSGLEN)
    integer(c_int64_t), intent(out) :: ids(BATCH)
    real(c_float),      intent(out) :: freq(BATCH), tstart(BATCH)
    integer(c_int),     intent(out) :: complete(BATCH)
    character(len=BATCH*MSGLEN) :: blocks
    logical(1) :: eom(BATCH)
    integer :: count, i

    call jtty_get_updates(blocks, ids, freq, tstart, eom, count)
    do i = 1, BATCH*MSGLEN
       text(i) = blocks(i:i)
    enddo
    complete = 0
    do i = 1, count
       if(eom(i)) complete(i) = 1
    enddo
    jtty_pako_get_updates = count
  end function jtty_pako_get_updates

  !> Encodes a message and synthesizes the audio to transmit.
  !>   msg/msglen : text to send (up to 80 characters)
  !>   f0         : audio frequency of the lowest tone, Hz
  !>   wave       : output buffer for float samples at 12000/s
  !>   maxwave    : size of wave (16 frames need 362496 samples)
  !>   canon      : out, 80 chars: the message exactly as it will be sent
  !>                (upper case, unsupported characters become '#')
  !> Returns the number of samples written, 0 if the text cannot be sent,
  !> or -n if wave is too small (n = samples needed).
  integer(c_int) function jtty_pako_encode(msg, msglen, f0, wave, maxwave, canon) &
       bind(C, name='jtty_pako_encode')
    integer(c_int), value :: msglen, maxwave
    character(kind=c_char), intent(in) :: msg(msglen)
    real(c_float),  value :: f0
    real(c_float),  intent(out) :: wave(maxwave)
    character(kind=c_char), intent(out) :: canon(MSGLEN)
    character(len=80) :: umsg
    integer :: itone(59*16), nsym, nwave, nsps1, icmplx, i
    real :: bt, fs, f01
    complex, allocatable :: cwave(:)
    real, allocatable :: w(:)

    jtty_pako_encode = 0
    umsg = ' '
    do i = 1, min(msglen, 80)
       umsg(i:i) = msg(i)
    enddo
    call genjtty(umsg, itone, nsym)      ! umsg comes back normalized
    do i = 1, MSGLEN
       canon(i) = umsg(i:i)
    enddo
    if(nsym <= 0) return

    nsps1 = NSPS
    nwave = nsps1*nsym
    if(nwave > maxwave) then
       jtty_pako_encode = -nwave
       return
    endif
    allocate(cwave(nwave), w(nwave))
    bt = 2.0; fs = real(RX_RATE); f01 = f0; icmplx = 0
    call gen_jttywave(itone, nsym, nsps1, bt, fs, f01, cwave, w, icmplx, nwave)
    wave(1:nwave) = w(1:nwave)
    jtty_pako_encode = nwave
  end function jtty_pako_encode

end module jtty_pako
