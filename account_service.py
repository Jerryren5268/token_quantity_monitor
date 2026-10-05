"""Private pipe service for the pixel pet. No timers and no startup network I/O."""
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import secrets
import sys
import time
import math
from datetime import datetime, timezone, timedelta
from concurrent.futures import ThreadPoolExecutor
from http.cookiejar import Cookie, CookieJar
from urllib.request import Request, build_opener, HTTPRedirectHandler, HTTPCookieProcessor
from urllib.error import HTTPError
from urllib.parse import urlparse

BASE = 'https://lmservice.lylab.sustcra.com'
DOMAIN = urlparse(BASE).hostname
SITE = 'https://newapi.513201.xyz/about-monitor/api/status'

class LoginRequired(ValueError):
    pass

class SafeRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if urlparse(newurl).netloc != urlparse(req.full_url).netloc:
            raise ValueError('请求跨站跳转，已停止。')
        return super().redirect_request(req, fp, code, msg, headers, newurl)

def protect(raw, decrypt=False):
    """DPAPI CurrentUser, with UI forbidden. Neither key nor plaintext is written."""
    class Blob(ctypes.Structure):
        _fields_ = [('length', wintypes.DWORD), ('data', ctypes.c_void_p)]
    buf = ctypes.create_string_buffer(raw)
    source = Blob(len(raw), ctypes.cast(buf, ctypes.c_void_p))
    target = Blob()
    crypt = ctypes.WinDLL('crypt32', use_last_error=True)
    fn = crypt.CryptUnprotectData if decrypt else crypt.CryptProtectData
    fn.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                   ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(Blob)]
    fn.restype = wintypes.BOOL
    free = ctypes.WinDLL('kernel32', use_last_error=True).LocalFree
    free.argtypes = [ctypes.c_void_p]
    free.restype = ctypes.c_void_p
    if not fn(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(target)):
        raise OSError('Windows 无法读取或保存加密会话。')
    try:
        return ctypes.string_at(target.data, target.length)
    finally:
        free(target.data)

class SessionStore:
    def __init__(self, directory=None):
        self.directory = Path(directory or Path(os.environ['LOCALAPPDATA']) / 'LMServiceQuota')
        self.path = self.directory / 'session.dpapi'
        self.profile_path = self.directory / 'account.dpapi'

    def load(self):
        if not self.path.exists():
            return None
        return json.loads(protect(self.path.read_bytes(), decrypt=True).decode('utf-8'))

    def save(self, data):
        self.write_encrypted(self.path, data)

    def write_encrypted(self, path, data):
        encrypted = protect(json.dumps(data, ensure_ascii=True).encode())
        self.directory.mkdir(parents=True, exist_ok=True)
        temp = path.with_suffix('.tmp-' + secrets.token_hex(4))
        try:
            temp.write_bytes(encrypted)
            os.replace(temp, path)
        finally:
            temp.unlink(missing_ok=True)

    def clear(self):
        self.path.unlink(missing_ok=True)

    def load_username(self):
        if not self.profile_path.exists():
            return ''
        data = json.loads(protect(self.profile_path.read_bytes(), decrypt=True).decode('utf-8'))
        return str(data.get('username') or '').strip() if data.get('version') == 1 else ''

    def save_username(self, username):
        self.write_encrypted(self.profile_path, {'version': 1, 'username': username})

    def forget_username(self):
        self.profile_path.unlink(missing_ok=True)

class Account:
    def __init__(self, store=None):
        self.store = store or SessionStore()
        self.cookies = CookieJar()
        self.http = build_opener(SafeRedirect(), HTTPCookieProcessor(self.cookies))
        self.public = build_opener(SafeRedirect())
        self.token = ''
        self.expiry = 0
        self.sid = ''
        self.flow = ''
        self.unit = None
        self.quotes = {}
        self.cache = {'site': None, 'personal': None}
        self.notice = ''
        self.username = ''
        self.pending_username = ''
        self.profile_notice = ''
        try:
            self.username = self.store.load_username()
        except Exception:
            self.profile_notice = '记住的账号无法读取，请重新输入账号。'
        try:
            saved = self.store.load()
            if saved and saved.get('version') == 1:
                self.token = str(saved.get('token') or '')
                self.expiry = float(saved.get('expiry') or 0)
                self.sid = str(saved.get('sid') or '')
                self.unit = saved.get('unit')
                if not self.username and saved.get('username'):
                    self.remember_username(saved['username'])
                for item in saved.get('cookies', []):
                    if item.get('domain', '').lstrip('.') == DOMAIN:
                        self.cookies.set_cookie(Cookie(**item))
        except Exception:
            self.token = ''; self.expiry = 0; self.sid = ''; self.cookies.clear()
            self.notice = '保存的登录状态无法读取，请重新登录。'

    def remember_username(self, username):
        username = str(username or '').strip()
        if not username:
            return
        if username == self.username and self.store.profile_path.exists():
            return
        self.username = username
        try:
            self.store.save_username(username)
            self.profile_notice = ''
        except Exception:
            self.profile_notice = '账号未能保存到本机，下次可能需要重新输入账号。'

    def save(self):
        cookies = []
        for c in self.cookies:
            if c.domain.lstrip('.') != DOMAIN:
                continue
            item = {key: getattr(c, key) for key in ('version','name','value','port','port_specified','domain','domain_specified','domain_initial_dot','path','path_specified','secure','expires','discard','comment','comment_url','rfc2109')}
            item['rest'] = c._rest
            cookies.append(item)
        try:
            self.store.save({'version': 1, 'token': self.token, 'expiry': self.expiry,
                             'sid': self.sid, 'unit': self.unit, 'cookies': cookies,
                             'username': self.username})
            self.notice = ''
        except Exception:
            self.notice = '已登录，但 Windows 未能保存会话；退出后需重新登录。'

    def clear(self):
        self.token = ''; self.sid = ''; self.expiry = 0; self.flow = ''
        self.pending_username = ''
        self.cookies.clear(); self.quotes.clear(); self.cache['personal'] = None
        try:
            self.store.clear()
            self.notice = ''
        except OSError:
            self.notice = '内存会话已清除，但磁盘会话删除失败。请关闭程序并检查文件权限。'

    def authenticated(self):
        return bool(self.token or any(c.name == 'new_api_refresh' and not c.is_expired() for c in self.cookies))

    def raw(self, path, body=None, auth=False):
        headers = {'User-Agent': 'Mozilla/5.0', 'Accept': 'application/json'}
        if auth and self.token: headers['Authorization'] = 'Bearer ' + self.token
        if path == '/api/user/auth/refresh' and self.sid: headers['X-Auth-Session'] = self.sid
        payload = None
        if body is not None:
            payload = json.dumps(body).encode('utf-8'); headers['Content-Type'] = 'application/json'
        req = Request(SITE if path == 'site' else BASE + path, data=payload, headers=headers)
        try:
            with (self.public if path == 'site' else self.http).open(req, timeout=20) as r:
                raw = r.read(2_000_001)
        except HTTPError as exc:
            if exc.code in (401,403): raise LoginRequired('登录已失效，请重新登录。') from None
            raise ValueError('网站暂时不可用（HTTP %s）。' % exc.code) from None
        if len(raw) > 2_000_000: raise ValueError('站点响应过大。')
        result = json.loads(raw)
        if result.get('success') is False:
            raise ValueError(str(result.get('message') or '网站拒绝了操作。')[:200])
        return result if path == 'site' else result.get('data', result)

    def rotate(self):
        try:
            data = self.raw('/api/user/auth/refresh', {})
            if not data.get('access_token'): raise LoginRequired('请重新登录。')
            self.accept(data)
        except LoginRequired:
            self.clear()
            raise

    def request(self, path, body=None, auth=False):
        rotated = False
        if auth:
            if not self.authenticated(): raise LoginRequired('请先登录个人账户。')
            if not self.token or self.expiry and self.expiry <= time.time()+10:
                self.rotate(); rotated = True
        try:
            result = self.raw(path, body, auth)
        except LoginRequired:
            if auth and body is None and not rotated:
                self.rotate()
                try: result = self.raw(path, None, True)
                except LoginRequired:
                    self.clear(); raise
            else:
                if auth: self.clear()
                raise
        if auth: self.save()
        return result

    def accept(self, data):
        if data.get('access_token'):
            self.token = data['access_token']; self.expiry = data.get('access_expires_at',0)
            self.sid = data.get('session',{}).get('sid',self.sid); self.flow = ''
            if self.pending_username:
                self.remember_username(self.pending_username)
                self.pending_username = ''
            self.save()
            return {'logged_in': True, 'username': self.username, 'message': self.notice or self.profile_notice or '登录成功，已记住账号与登录状态。'}
        if data.get('flow_token'):
            self.flow = data['flow_token']
            return {'needs_2fa': True, 'username': self.pending_username, 'message': '请输入验证器验证码。'}
        raise ValueError('登录未返回有效会话，请检查账户验证要求。')

    def login(self, username, password):
        if not username.strip() or not password: raise ValueError('请输入账号和密码。')
        settings = self.request('/api/status')
        if settings.get('turnstile_check'): raise ValueError('网站要求网页验证码，当前桌宠无法完成该验证。')
        if settings.get('password_login_encryption_enabled'): raise ValueError('网站启用了额外密码加密，需要更新登录组件。')
        self.clear(); self.unit = settings.get('quota_per_unit')
        self.pending_username = username.strip()
        try:
            return self.accept(self.request('/api/user/login?turnstile=', {'username':username.strip(),'password':password}))
        except Exception:
            self.pending_username = ''
            raise

    def personal(self):
        user = self.request('/api/user/self', auth=True)
        # Migrate existing sessions without requiring another password login.
        if user.get('username') and str(user['username']).strip() != self.username:
            self.remember_username(user['username'])
            self.save()
        if not self.unit:
            self.unit = self.request('/api/status').get('quota_per_unit'); self.save()
        result = {'balance':user.get('quota'), 'used':user.get('used_quota'), 'unit':self.unit, 'subscriptions':[], 'subscription_error':None}
        try:
            data = self.request('/api/subscription/self', auth=True)
            rows = data if isinstance(data,list) else data.get('subscriptions',data.get('user_subscriptions',data.get('all_subscriptions',[])))
            for row in rows:
                s = row.get('subscription',row)
                result['subscriptions'].append({'id':s.get('id'),'title':row.get('plan',{}).get('title'),
                    'total':s.get('amount_total',s.get('total_amount')), 'used':s.get('amount_used',s.get('used_amount')),
                    'ends':s.get('end_time'), 'status':s.get('status'),
                    'next_reset':s.get('next_reset_time'),
                    'reset_period':row.get('plan',{}).get('quota_reset_period')})
        except LoginRequired:
            raise
        except Exception:
            result['subscription_error'] = '订阅额度读取失败，钱包余额已更新。'
        result['daily_usage'] = self.daily_usage()
        return result

    def daily_usage(self):
        # Query the account's consumption log aggregate, never a balance delta.
        now = datetime.now(timezone(timedelta(hours=8)))
        day = now.date().isoformat()
        start = int(now.replace(hour=0, minute=0, second=0, microsecond=0).timestamp())
        end = int(now.timestamp())
        try:
            data = self.request('/api/log/self/stat?type=2&start_timestamp=%d&end_timestamp=%d' % (start, end), auth=True)
            quota = data.get('quota') if isinstance(data, dict) else None
            if isinstance(quota, bool) or not isinstance(quota, (int, float)) or not math.isfinite(quota) or quota < 0:
                raise ValueError('今日用量统计不可用。')
            return {'date':day, 'quota':quota, 'fetched_at':time.time(), 'stale':False, 'error':None}
        except LoginRequired:
            raise
        except Exception:
            previous = ((self.cache.get('personal') or {}).get('data') or {}).get('daily_usage') or {}
            if previous.get('date') == day and previous.get('quota') is not None:
                return {**previous, 'stale':True, 'error':'今日用量更新失败。'}
            return {'date':day, 'quota':None, 'fetched_at':None, 'stale':True, 'error':'今日用量暂不可用。'}

    def site(self):
        payload = self.request('site'); q = payload.get('sources',{}).get('quota',{})
        windows = []
        for group in q.get('data',{}).get('groups',[]):
            for w in group.get('windows',[]):
                windows.append({'name':group.get('name','主额度'),'period':w.get('label',''),
                    'used_percent':w.get('utilization'),'resets':w.get('resets_at')})
        if not windows: raise ValueError('上游暂无额度数据。')
        return {'windows':windows, 'collected_at':q.get('last_success_at') or payload.get('generated_at'),
                'upstream_stale':bool(q.get('stale') or q.get('ok') is False)}

    def snapshot(self, key):
        if key == 'personal' and not self.authenticated():
            return {'data':None,'fetched_at':None,'stale':False,'error':'请先登录个人账户。'}
        try:
            data = self.site() if key == 'site' else self.personal()
            current = {'data':data,'fetched_at':time.time(),'stale':False,'error':None}
            self.cache[key] = current
            return current
        except Exception as exc:
            old = self.cache[key] or {'data':None,'fetched_at':None}
            error = str(exc) if isinstance(exc,ValueError) else '连接失败，请稍后刷新。'
            return {**old,'stale':True,'error':error[:200]}

    def plans(self):
        data = self.request('/api/subscription/plans',auth=True)
        return data if isinstance(data,list) else data.get('plans',data.get('items',[]))

    def dispatch(self, args):
        action = args.get('action')
        if action == 'state': return {'logged_in':self.authenticated(),'username':self.username,'message':self.notice or self.profile_notice}
        if action == 'summary':
            with ThreadPoolExecutor(max_workers=2) as pool:
                site = pool.submit(self.snapshot,'site'); personal = pool.submit(self.snapshot,'personal')
                return {'site':site.result(),'personal':personal.result(),'logged_in':self.authenticated(),'username':self.username,'message':self.notice or self.profile_notice}
        if action == 'login': return self.login(args.get('username',''),args.get('password',''))
        if action == 'verify':
            if not self.flow: raise ValueError('请先输入账号密码登录。')
            return self.accept(self.request('/api/user/login/verify',{'flow_token':self.flow,'method':'2fa','code':args.get('code','').strip()}))
        if action == 'logout':
            self.clear(); return {'logged_in':False,'username':self.username,'message':self.notice or '已退出登录，已保留账号；下次只需输入密码。'}
        if action == 'forget_username':
            self.store.forget_username()
            self.username = ''; self.profile_notice = ''
            if self.authenticated():
                self.save()
            return {'username':'','message':'已清除记住的账号。'}
        if action == 'plans':
            rows = self.plans(); self.quotes.clear(); output = []
            for row in rows:
                plan = row.get('plan',row); price = plan.get('price_amount',plan.get('price'))
                if plan.get('enabled') is False or price is None: continue
                quote = secrets.token_urlsafe(24)
                self.quotes[quote] = (dict(plan),time.monotonic()+300)
                output.append({'quote':quote,'title':plan.get('title','套餐'),'price':str(price)+' '+str(plan.get('currency','USD'))})
            return {'plans':output,'message':'套餐以网站当前价格为准。'}
        if action == 'buy':
            if args.get('confirmed') is not True: raise ValueError('请先确认订阅。')
            quote = self.quotes.pop(args.get('quote',''),None)
            if not quote or quote[1]<time.monotonic(): raise ValueError('确认已使用或过期，请先核实钱包再重新读取套餐。')
            plan=quote[0]
            current=next((r.get('plan',r) for r in self.plans() if str(r.get('plan',r).get('id'))==str(plan.get('id'))),None)
            fields=('title','price','price_amount','currency','duration_value','duration_unit','total_amount')
            if not current or current.get('enabled') is False or any(current.get(k)!=plan.get(k) for k in fields):
                raise ValueError('套餐已改变，请重新读取后确认。')
            try: self.request('/api/subscription/balance/pay',{'plan_id':int(plan['id'])},auth=True)
            except Exception: raise ValueError('未能确认订阅结果，请先核实钱包是否生效，避免重复提交。') from None
            return {'message':'订阅请求成功。'}
        raise ValueError('未知操作。')

if __name__ == '__main__':
    account = Account()
    for line in sys.stdin:
        args = None
        try:
            args = json.loads(line)
            result = {'ok':True,'data':account.dispatch(args)}
        except Exception as exc:
            result = {'ok':False,'error':str(exc)[:220] if isinstance(exc,ValueError) else '操作失败，请稍后重试。','logged_in':account.authenticated()}
        finally:
            if isinstance(args,dict): args.clear()
            line = ''; args = None
        print(json.dumps(result,ensure_ascii=True),flush=True)
