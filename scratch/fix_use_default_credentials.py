import os

def process_dir(d):
    for root, dirs, files in os.walk(d):
        for f in files:
            if f.endswith('.cs'):
                p = os.path.join(root, f)
                with open(p, 'r', encoding='utf-8') as file:
                    content = file.read()
                
                new_content = content.replace('UseDefaultCredentials = true,', '').replace('UseDefaultCredentials = true', '')
                if new_content != content:
                    with open(p, 'w', encoding='utf-8') as file:
                        file.write(new_content)
                    print(f'Fixed {p}')

process_dir(r'c:\Users\adm-kh\source\repos\EZKPM\EZKPM.Client.Desktop')
